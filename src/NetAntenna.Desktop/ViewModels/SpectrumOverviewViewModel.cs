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
    
    // Dynamically populated from device lineup
    public ObservableCollection<PhysicalChannelGroup> Channels { get; } = new();

    public SpectrumOverviewViewModel(
        ITunerClient tunerClient, 
        IDatabaseService db, 
        ILogger<SpectrumOverviewViewModel> logger)
    {
        _tunerClient = tunerClient;
        _db = db;
        _logger = logger;
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
            // Clear existing lock status, but keep grid structure if already loaded
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
            // and aggregate virtual channels and their names.
            var lineup = await _tunerClient.GetLineupAsync(device.BaseUrl);

            // Build a lookup: physical_channel_number -> PhysicalChannelBuilder
            var groups = new Dictionary<int, PhysicalChannelBuilder>();
            foreach (var ch in lineup)
            {
                // GuideNumber is like "44.2" - the major part is the physical channel (ATSC major#)
                var dot = ch.GuideNumber.IndexOf('.');
                var majorStr = dot >= 0 ? ch.GuideNumber[..dot] : ch.GuideNumber;
                if (!int.TryParse(majorStr, out var physCh)) continue;

                var ss = ch.SignalStrength ?? 0;
                var sq = ch.SignalQuality ?? 0;
                var hasLock = ss > 0 || sq > 0;

                if (!groups.TryGetValue(physCh, out var group))
                {
                    group = new PhysicalChannelBuilder();
                    groups[physCh] = group;
                }

                // Keep the best signal levels seen on this physical channel
                group.BestSs = Math.Max(group.BestSs, ss);
                group.BestSq = Math.Max(group.BestSq, sq);
                group.HasLock |= hasLock;

                // Collect the networks and virtual channels
                if (!string.IsNullOrWhiteSpace(ch.GuideNumber))
                    group.VirtualChannels.Add(ch.GuideNumber);
                if (!string.IsNullOrWhiteSpace(ch.GuideName))
                    group.Networks.Add(ch.GuideName);
            }

            _logger.LogInformation("Lineup returned {Count} virtual channels across {Physical} physical channels.",
                lineup.Count, groups.Count);

            // Rebuild the UI grid on the main thread
            var orderedPhysicals = groups.Keys.OrderBy(c => c).ToList();

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var currentKeys = Channels.Select(c => c.PhysicalChannel).ToHashSet();
                
                // If the channel set changed completely (or is empty), clear and rebuild
                if (!currentKeys.SetEquals(orderedPhysicals))
                {
                    Channels.Clear();
                    foreach (var phys in orderedPhysicals)
                    {
                        Channels.Add(new PhysicalChannelGroup { PhysicalChannel = phys });
                    }
                }

                // Update the signal values and aggregated strings for all rows
                for (int i = 0; i < Channels.Count; i++)
                {
                    var ch = Channels[i];
                    if (groups.TryGetValue(ch.PhysicalChannel, out var group))
                    {
                        ch.SignalStrength = group.BestSs;
                        ch.SymbolQuality = group.BestSq;
                        ch.HasLock = group.HasLock;
                        ch.VirtualChannels = string.Join(", ", group.VirtualChannels);
                        ch.Networks = string.Join(", ", group.Networks);
                    }
                    ProgressPercent = (int)((i + 1) / (float)Channels.Count * 100);
                }
            });

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

public partial class PhysicalChannelGroup : ObservableObject
{
    [ObservableProperty] private int _physicalChannel;
    [ObservableProperty] private int _signalStrength;
    [ObservableProperty] private int _symbolQuality;
    [ObservableProperty] private bool _hasLock;
    [ObservableProperty] private string _virtualChannels = "";
    [ObservableProperty] private string _networks = "";
}

internal class PhysicalChannelBuilder
{
    public int BestSs { get; set; }
    public int BestSq { get; set; }
    public bool HasLock { get; set; }
    public List<string> VirtualChannels { get; } = new();
    public List<string> Networks { get; } = new();
}
