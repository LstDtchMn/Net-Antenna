using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NetAntenna.Core.Data;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public partial class SpectrumOverviewViewModel : ViewModelBase
{
    private readonly ITunerClient _tunerClient;
    private readonly IDatabaseService _db;
    private readonly ILogger<SpectrumOverviewViewModel> _logger;

    [ObservableProperty] private bool _isSweeping;
    [ObservableProperty] private int _currentSweepChannel;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private bool _isRescanning;
    [ObservableProperty] private string _rescanStatusText = "";
    
    // Channels 14 -> 36 are the modern post-repack UHF TV band
    public ObservableCollection<SpectrumChannel> Channels { get; } = new();

    public SpectrumOverviewViewModel(
        ITunerClient tunerClient, 
        IDatabaseService db, 
        ILogger<SpectrumOverviewViewModel> logger)
    {
        _tunerClient = tunerClient;
        _db = db;
        _logger = logger;
        
        // Initialize the UHF grid
        for (int i = 14; i <= 36; i++)
        {
            Channels.Add(new SpectrumChannel { ChannelNumber = i });
        }
    }

    [RelayCommand]
    private async Task StartSweepAsync()
    {
        if (IsSweeping) return;

        _logger.LogInformation("Starting Spectrum Sweep via lineup.json...");
        var devices = await _db.GetAllDevicesAsync();
        var device = devices.FirstOrDefault();
        if (device == null)
        {
            _logger.LogWarning("No HDHomeRun devices found. Cannot perform sweep.");
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsSweeping = true;
            ProgressPercent = 0;
            foreach (var ch in Channels)
            {
                ch.SignalStrength = 0;
                ch.SymbolQuality = 0;
                ch.HasLock = false;
            }
        });

        try
        {
            // The HDHomeRun lineup.json already contains SignalStrength and SignalQuality
            // for every channel it knows about. Group by physical channel (major guide number)
            // and take the best signal value per physical channel number.
            var lineup = await _tunerClient.GetLineupAsync(device.BaseUrl);

            // Build a lookup: physical_channel_number -> best SS and SQ seen on that channel
            var signalByPhysical = new Dictionary<int, (int Ss, int Sq, bool HasLock)>();
            foreach (var ch in lineup)
            {
                // GuideNumber is like "44.2" - the major part is the physical channel (ATSC major#)
                var dot = ch.GuideNumber.IndexOf('.');
                var majorStr = dot >= 0 ? ch.GuideNumber[..dot] : ch.GuideNumber;
                if (!int.TryParse(majorStr, out var physCh)) continue;

                var ss = ch.SignalStrength ?? 0;
                var sq = ch.SignalQuality ?? 0;
                var hasLock = ss > 0 || sq > 0;

                if (!signalByPhysical.TryGetValue(physCh, out var existing) || ss > existing.Ss)
                {
                    signalByPhysical[physCh] = (ss, sq, hasLock);
                }
            }

            _logger.LogInformation("Lineup returned {Count} channels across {Physical} physical channels.",
                lineup.Count, signalByPhysical.Count);

            // Update each SpectrumChannel grid cell
            for (int i = 0; i < Channels.Count; i++)
            {
                var ch = Channels[i];
                if (signalByPhysical.TryGetValue(ch.ChannelNumber, out var sig))
                {
                    _logger.LogInformation("Ch {Num}: SS={Ss}, SQ={Sq}, Lock={Lock}",
                        ch.ChannelNumber, sig.Ss, sig.Sq, sig.HasLock);

                    var capturedSig = sig; // capture for lambda
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ch.SignalStrength = capturedSig.Ss;
                        ch.SymbolQuality = capturedSig.Sq;
                        ch.HasLock = capturedSig.HasLock;
                    });
                }

                var capturedI = i;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    ProgressPercent = (int)((capturedI + 1) / (float)Channels.Count * 100));
            }

            _logger.LogInformation("Spectrum Sweep Completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during the spectrum sweep.");
        }
        finally
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsSweeping = false;
                ProgressPercent = 100;
            });
        }
    }

    [RelayCommand]
    private async Task RescanDeviceAsync()
    {
        if (IsRescanning || IsSweeping) return;

        var devices = await _db.GetAllDevicesAsync();
        var device = devices.FirstOrDefault();
        if (device == null)
        {
            _logger.LogWarning("No HDHomeRun devices found. Cannot start rescan.");
            return;
        }

        _logger.LogInformation("Triggering device-level channel scan at {Url}...", device.BaseUrl);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsRescanning = true;
            RescanStatusText = "Starting scan...";
        });

        try
        {
            await _tunerClient.StartLineupScanAsync(device.BaseUrl);

            // Poll until the device reports scan complete
            while (true)
            {
                await Task.Delay(1500);
                var status = await _tunerClient.GetLineupScanStatusAsync(device.BaseUrl);

                var pct = status.Progress;
                var found = status.Found;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    RescanStatusText = status.IsScanning
                        ? $"Scanning... {pct}% ({found} channels found)"
                        : $"Scan complete — {found} channels found");

                _logger.LogInformation("Device scan progress: {Pct}%, found={Found}, inProgress={In}",
                    pct, found, status.ScanInProgress);

                if (!status.IsScanning) break;
            }

            _logger.LogInformation("Device scan completed. Refreshing sweep...");

            // Refresh the sweep view with freshly scanned lineup data
            await StartSweepAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during device channel rescan.");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                RescanStatusText = $"Scan failed: {ex.Message}");
        }
        finally
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => IsRescanning = false);
        }
    }
}

public partial class SpectrumChannel : ObservableObject
{
    [ObservableProperty] private int _channelNumber;
    [ObservableProperty] private int _signalStrength;
    [ObservableProperty] private int _symbolQuality;
    [ObservableProperty] private bool _hasLock;

    /// <summary>US UHF center frequency in MHz for display purposes.</summary>
    public int FrequencyMhz => 473 + (ChannelNumber - 14) * 6;
}

internal static class UhfChannelHelper
{
    /// <summary>Converts a US UHF TV physical channel number to its center frequency in Hz.</summary>
    public static long GetFrequencyHz(int channel)
    {
        // UHF Ch2-6: low VHF, 7-13: high VHF, 14-36: UHF
        if (channel >= 14)
            return (473_000_000L + (long)(channel - 14) * 6_000_000L);
        // VHF fallback (not used by this app)
        return 0;
    }
}
