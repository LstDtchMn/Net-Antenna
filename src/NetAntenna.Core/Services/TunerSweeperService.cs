using Microsoft.Extensions.Logging;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

public sealed class TunerSweeperService : ITunerSweeperService, IDisposable
{
    private readonly IDatabaseService _db;
    private readonly ITunerClient _client;
    private readonly ILogger<TunerSweeperService> _logger;
    private readonly ISignalLogger _signalLogger;

    private CancellationTokenSource? _sweepCts;
    private SweeperStatus _status = new();
    private readonly object _lock = new();

    private bool _wasLoggerRunningBeforeSweep;

    public event EventHandler<SweeperStatus>? StatusChanged;

    public SweeperStatus CurrentStatus
    {
        get { lock (_lock) { return _status; } }
    }

    public TunerSweeperService(IDatabaseService db, ITunerClient client, ISignalLogger signalLogger, ILogger<TunerSweeperService> logger)
    {
        _db = db;
        _client = client;
        _signalLogger = signalLogger;
        _logger = logger;
    }

    public async Task StartSweepAsync(
        string deviceId,
        IReadOnlyList<int> tunerIndices,
        TimeSpan dwellTime,
        SweepProfile profile)
    {
        _logger.LogInformation(
            "Starting Active Signal Sweep on device {DeviceId} with {Count} tuners, mode={Mode}",
            deviceId, tunerIndices.Count, profile.Mode);

        lock (_lock)
        {
            if (_status.IsSweeping)
                throw new InvalidOperationException("A sweep is already in progress.");
        }

        var device = await _db.GetDeviceAsync(deviceId);
        if (device == null) throw new InvalidOperationException($"Device {deviceId} not found.");

        var allChannels = await _db.GetChannelLineupAsync(deviceId);
        var activeChannels = allChannels.Where(c => !c.IsHidden).ToList();

        if (activeChannels.Count == 0 || tunerIndices.Count == 0)
        {
            _logger.LogWarning("No active channels or tuners selected for sweep.");
            return;
        }

        lock (_lock)
        {
            _sweepCts = new CancellationTokenSource();
            _status = new SweeperStatus
            {
                IsSweeping = true,
                TotalChannels = activeChannels.Count,
                ChannelsScanned = 0,
                CurrentRun = 1,
                TargetRuns = profile.Mode == SweepMode.FixedCount ? profile.MaxRuns : null
            };
        }

        _wasLoggerRunningBeforeSweep = _signalLogger.IsRunning;
        if (!_signalLogger.IsRunning)
        {
            _logger.LogInformation("SignalLoggerService is not running. Starting it for the duration of the sweep.");
            // Poll every 2 seconds during active sweeps to gather richer data points
            await _signalLogger.StartAsync(device.DeviceId, device.BaseUrl, device.TunerCount, TimeSpan.FromSeconds(2));
        }

        OnStatusChanged();

        _ = Task.Run(() => RunMultiSweepAsync(device, activeChannels, tunerIndices, dwellTime, profile, _sweepCts.Token));
    }

    public void CancelSweep()
    {
        lock (_lock)
        {
            if (!_status.IsSweeping) return;
            _sweepCts?.Cancel();
        }
    }

    // -----------------------------------------------------------------------
    // Multi-pass outer loop
    // -----------------------------------------------------------------------

    private async Task RunMultiSweepAsync(
        HdHomeRunDevice device,
        IReadOnlyList<ChannelInfo> channels,
        IReadOnlyList<int> tuners,
        TimeSpan dwellTime,
        SweepProfile profile,
        CancellationToken ct)
    {
        var sessionStart = DateTimeOffset.UtcNow;
        var completedRuns = new List<SweepRunResult>();
        int runNumber = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // --- Stop condition checks ---
                if (profile.Mode == SweepMode.FixedCount && runNumber >= profile.MaxRuns) break;
                if (profile.Mode == SweepMode.TimeLimited &&
                    DateTimeOffset.UtcNow - sessionStart >= profile.MaxDuration) break;

                runNumber++;
                _logger.LogInformation("Starting sweep pass {Run}", runNumber);

                var runStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // --- Run a single full pass ---
                await RunSinglePassAsync(device, channels, tuners, dwellTime, runStart, runNumber, completedRuns, ct);

                if (ct.IsCancellationRequested) break;

                var runEnd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                // Snapshot this pass
                var passStats = await _db.GetChannelStatisticsAsync(device.DeviceId, runStart, runEnd);
                var runResult = new SweepRunResult
                {
                    RunNumber = runNumber,
                    StartMs = runStart,
                    EndMs = runEnd,
                    Results = passStats
                };
                completedRuns.Add(runResult);

                // Aggregate across all runs so far
                var allTimeStats = AggregateRuns(completedRuns);

                lock (_lock)
                {
                    _status = new SweeperStatus
                    {
                        IsSweeping = true,
                        TotalChannels = channels.Count,
                        ChannelsScanned = channels.Count,
                        CurrentRun = runNumber,
                        TargetRuns = profile.Mode == SweepMode.FixedCount ? profile.MaxRuns : null,
                        LiveResults = allTimeStats,
                        CompletedRuns = completedRuns.AsReadOnly()
                    };
                }
                OnStatusChanged();
            }
        }
        catch (OperationCanceledException) { _logger.LogInformation("Sweep cancelled."); }
        catch (Exception ex) { _logger.LogError(ex, "Error during sweep."); }
        finally
        {
            if (!_wasLoggerRunningBeforeSweep && _signalLogger.IsRunning)
            {
                _logger.LogInformation("Stopping SignalLoggerService because it was started by the sweeper.");
                await _signalLogger.StopAsync();
            }

            foreach (var t in tuners)
            {
                try { await _client.SetChannelAsync(device.BaseUrl, t, "none", CancellationToken.None); }
                catch { }
            }

            var allTimeStats = AggregateRuns(completedRuns);

            lock (_lock)
            {
                _status = new SweeperStatus
                {
                    IsSweeping = false,
                    TotalChannels = channels.Count,
                    ChannelsScanned = channels.Count,
                    CurrentRun = runNumber,
                    TargetRuns = profile.Mode == SweepMode.FixedCount ? profile.MaxRuns : null,
                    LiveResults = allTimeStats,
                    CompletedRuns = completedRuns.AsReadOnly()
                };
            }
            OnStatusChanged();
            _sweepCts?.Dispose();
            _sweepCts = null;
            // Record the session history
            try
            {
                var sessionEnd = DateTimeOffset.UtcNow;
                var session = new SweepSession
                {
                    DeviceId = device.DeviceId,
                    StartTimeUnixMs = sessionStart.ToUnixTimeMilliseconds(),
                    EndTimeUnixMs = sessionEnd.ToUnixTimeMilliseconds(),
                    Mode = profile.Mode.ToString(),
                    TargetRuns = profile.Mode == SweepMode.FixedCount ? profile.MaxRuns : 0,
                    CompletedRuns = runNumber
                };
                await _db.RecordSweepSessionAsync(session);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record SweepSession to database.");
            }

            _logger.LogInformation("All sweep passes complete. Total runs: {Runs}", runNumber);
        }
    }

    // -----------------------------------------------------------------------
    // Single pass — tunes all channels across all selected tuners
    // -----------------------------------------------------------------------

    private async Task RunSinglePassAsync(
        HdHomeRunDevice device,
        IReadOnlyList<ChannelInfo> channels,
        IReadOnlyList<int> tuners,
        TimeSpan dwellTime,
        long sweepStartMs,
        int currentRun,
        IReadOnlyList<SweepRunResult> completedRuns,
        CancellationToken ct)
    {
        var chunks = Partition(channels, tuners.Count);
        int tunersUsed = Math.Min(tuners.Count, chunks.Count);

        // Reset per-pass channel counter
        lock (_lock)
        {
            _status = new SweeperStatus
            {
                IsSweeping = true,
                TotalChannels = channels.Count,
                ChannelsScanned = 0,
                CurrentRun = currentRun,
                TargetRuns = _status.TargetRuns,
                LiveResults = _status.LiveResults,
                CompletedRuns = completedRuns
            };
        }
        OnStatusChanged();

        var tasks = new List<Task>();
        for (int i = 0; i < tunersUsed; i++)
        {
            tasks.Add(SweepTunerAsync(device, tuners[i], chunks[i], dwellTime, sweepStartMs, currentRun, completedRuns, ct));
        }
        await Task.WhenAll(tasks);
    }

    // -----------------------------------------------------------------------
    // Single tuner worker — dwells on each assigned channel
    // -----------------------------------------------------------------------

    private async Task SweepTunerAsync(
        HdHomeRunDevice device,
        int tunerIndex,
        IReadOnlyList<ChannelInfo> channels,
        TimeSpan dwellTime,
        long sweepStartMs,
        int currentRun,
        IReadOnlyList<SweepRunResult> completedRuns,
        CancellationToken ct)
    {
        foreach (var channel in channels)
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogDebug("[Run {Run}] Tuner {Tuner} → channel {Ch}", currentRun, tunerIndex, channel.GuideNumber);

            try
            {
                await _client.SetChannelAsync(device.BaseUrl, tunerIndex, "v" + channel.GuideNumber, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Tune failed: tuner {Tuner} channel {Ch}", tunerIndex, channel.GuideNumber);
            }

            try { await Task.Delay(dwellTime, ct); }
            catch (OperationCanceledException) { throw; }

            // Grab live stats from the beginning of this run to now
            var toMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var runStats = await _db.GetChannelStatisticsAsync(device.DeviceId, sweepStartMs, toMs);
            var allTimeStats = AggregateRuns(completedRuns, runStats);

            lock (_lock)
            {
                _status = new SweeperStatus
                {
                    IsSweeping = true,
                    TotalChannels = _status.TotalChannels,
                    ChannelsScanned = _status.ChannelsScanned + 1,
                    CurrentRun = currentRun,
                    TargetRuns = _status.TargetRuns,
                    LiveResults = allTimeStats,
                    CompletedRuns = completedRuns
                };
            }
            OnStatusChanged();
        }
    }

    // -----------------------------------------------------------------------
    // Aggregation helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Merges all completed run results with an optional current-run overlay
    /// to produce an all-time Min/Max/Avg per channel.
    /// </summary>
    private static IReadOnlyList<ChannelStatistics> AggregateRuns(
        IReadOnlyList<SweepRunResult> completedRuns,
        IReadOnlyList<ChannelStatistics>? currentRunStats = null)
    {
        // Collect all per-channel data points
        var byChannel = new Dictionary<string, List<ChannelStatistics>>();

        foreach (var run in completedRuns)
        {
            foreach (var s in run.Results)
            {
                if (!byChannel.TryGetValue(s.Channel, out var list))
                    byChannel[s.Channel] = list = new();
                list.Add(s);
            }
        }

        // Also include in-progress current run
        if (currentRunStats != null)
        {
            foreach (var s in currentRunStats)
            {
                if (!byChannel.TryGetValue(s.Channel, out var list))
                    byChannel[s.Channel] = list = new();
                list.Add(s);
            }
        }

        return byChannel.Select(kv =>
        {
            var entries = kv.Value;
            return new ChannelStatistics
            {
                Channel = kv.Key,
                SampleCount = entries.Sum(e => e.SampleCount),
                AvgSs  = entries.Average(e => e.AvgSs),
                MinSs  = entries.Min(e => e.MinSs),
                MaxSs  = entries.Max(e => e.MaxSs),
                AvgSnq = entries.Average(e => e.AvgSnq),
                MinSnq = entries.Min(e => e.MinSnq),
                MaxSnq = entries.Max(e => e.MaxSnq),
                AvgSeq = entries.Average(e => e.AvgSeq),
                MinSeq = entries.Min(e => e.MinSeq),
                MaxSeq = entries.Max(e => e.MaxSeq),
            };
        })
        .OrderBy(s => s.Channel)
        .ToList();
    }

    private static List<List<T>> Partition<T>(IReadOnlyList<T> source, int partitions)
    {
        var result = new List<List<T>>();
        for (int i = 0; i < partitions; i++) result.Add(new List<T>());
        for (int i = 0; i < source.Count; i++) result[i % partitions].Add(source[i]);
        return result.Where(l => l.Count > 0).ToList();
    }

    private void OnStatusChanged() => StatusChanged?.Invoke(this, CurrentStatus);

    public void Dispose()
    {
        _sweepCts?.Cancel();
        _sweepCts?.Dispose();
    }
}
