using NetAntenna.Core.Data;
using NetAntenna.Core.Models;

namespace NetAntenna.Core.Services;

/// <summary>
/// Background service that continuously polls HDHomeRun tuners and logs signal data to SQLite.
/// Batches writes for performance (every 10 samples or 30 seconds, whichever comes first).
/// </summary>
public sealed class SignalLoggerService : ISignalLogger, IDisposable
{
    private readonly ITunerClient _tunerClient;
    private readonly IDatabaseService _database;

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private readonly List<SignalSample> _sampleBuffer = new();
    private readonly object _bufferLock = new();
    private DateTime _lastFlush = DateTime.UtcNow;

    private const int BatchSize = 10;
    private static readonly TimeSpan MaxFlushInterval = TimeSpan.FromSeconds(30);

    public event EventHandler<SignalSample>? SignalSampleReceived;

    public bool IsRunning { get; private set; }

    public SignalLoggerService(ITunerClient tunerClient, IDatabaseService database)
    {
        _tunerClient = tunerClient;
        _database = database;
    }

    /// <inheritdoc />
    public Task StartAsync(string deviceId, string baseUrl, int tunerCount,
        TimeSpan interval, CancellationToken ct = default)
    {
        if (IsRunning)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsRunning = true;
        _pollingTask = PollLoopAsync(deviceId, baseUrl, tunerCount, interval, _cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (!IsRunning || _cts is null)
            return;

        _cts.Cancel();
        IsRunning = false;

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        // Flush any remaining buffered samples
        await FlushBufferAsync();

        _cts.Dispose();
        _cts = null;
    }

    private async Task PollLoopAsync(
        string deviceId, string baseUrl, int tunerCount,
        TimeSpan interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Poll all tuners
                for (int i = 0; i < tunerCount; i++)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        var status = await _tunerClient.GetTunerStatusAsync(baseUrl, i, ct);
                        var sample = SignalSample.FromTunerStatus(deviceId, i, status);

                        // Raise event for real-time UI updates
                        SignalSampleReceived?.Invoke(this, sample);

                        // Buffer for batched DB writes
                        lock (_bufferLock)
                        {
                            _sampleBuffer.Add(sample);
                        }
                    }
                    catch (HttpRequestException)
                    {
                        // Tuner may be temporarily unreachable; skip this poll
                    }
                    catch (TaskCanceledException)
                    {
                        // Timeout or cancellation; skip
                    }
                }

                // Check if we should flush the buffer
                bool shouldFlush;
                lock (_bufferLock)
                {
                    shouldFlush = _sampleBuffer.Count >= BatchSize ||
                                  DateTime.UtcNow - _lastFlush >= MaxFlushInterval;
                }

                if (shouldFlush)
                {
                    await FlushBufferAsync();
                }

                // Wait for the next polling interval
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Log error but don't crash the polling loop
                // Wait a bit before retrying
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task FlushBufferAsync()
    {
        List<SignalSample> samplesToFlush;
        lock (_bufferLock)
        {
            if (_sampleBuffer.Count == 0) return;
            samplesToFlush = new List<SignalSample>(_sampleBuffer);
            _sampleBuffer.Clear();
            _lastFlush = DateTime.UtcNow;
        }

        try
        {
            await _database.InsertSamplesAsync(samplesToFlush);
        }
        catch (Exception)
        {
            // If DB write fails, we lose these samples but don't crash
            // In a future version, we could retry or write to a fallback file
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
