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
        
        _logger.LogInformation("Starting Spectrum Sweep...");
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
            // Perform the sweep using Tuner 0
            for (int i = 0; i < Channels.Count; i++)
            {
                var ch = Channels[i];
                Avalonia.Threading.Dispatcher.UIThread.Post(() => CurrentSweepChannel = ch.ChannelNumber);
                
                _logger.LogInformation("Sweeping Channel {Channel}...", ch.ChannelNumber);
                
                try
                {
                    // HDHomeRun HTTP API: tune via /tunerN/ch<frequency_hz>
                    // UHF Ch14 = 473 MHz; each channel is 6 MHz apart
                    var freqHz = GetUhfFrequencyHz(ch.ChannelNumber);
                    await _tunerClient.SetChannelAsync(device.BaseUrl, 0, $"ch{freqHz}");
                    // Wait 2 seconds for the tuner to search for and acquire a lock
                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to command tuner to channel {Channel}", ch.ChannelNumber);
                    continue; // Skip trying to read status if tuning failed
                }

                var status = await _tunerClient.GetTunerStatusAsync(device.BaseUrl, 0);
                if (status != null)
                {
                    _logger.LogInformation("Channel {Channel} Status - Lock: {Lock}, SS: {SS}, SEQ: {SEQ}", 
                        ch.ChannelNumber, status.Lock, status.SignalStrength, status.SymbolQuality);

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ch.SignalStrength = status.SignalStrength;
                        ch.SymbolQuality = status.SymbolQuality;
                        ch.HasLock = status.Lock != "none";
                    });
                }
                else
                {
                    _logger.LogWarning("Failed to get tuner status for Channel {Channel}", ch.ChannelNumber);
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ProgressPercent = (int)((i + 1) / (float)Channels.Count * 100);
                });
            }
            _logger.LogInformation("Spectrum Sweep Completed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during the spectrum sweep.");
        }
        finally
        {
            _logger.LogInformation("Releasing Tuner 0...");
            // Release tuner
            if (device != null)
            {
                try
                {
                    await _tunerClient.SetChannelAsync(device.BaseUrl, 0, "none");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to release tuner 0.");
                }
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsSweeping = false;
                ProgressPercent = 100;
            });
        }
    }

    private static long GetUhfFrequencyHz(int channel)
        => 473_000_000L + (long)(channel - 14) * 6_000_000L;
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
