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
                
                // Command tuner to tune to physical channel
                await _tunerClient.SetChannelAsync(device.BaseUrl, 0, $"8vsb:{ch.ChannelNumber}");
                
                // Wait 1 second for the tuner to acquire a lock
                await Task.Delay(1000);

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
                await _tunerClient.SetChannelAsync(device.BaseUrl, 0, "none");
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsSweeping = false;
                ProgressPercent = 100;
            });
        }
    }
}

public partial class SpectrumChannel : ObservableObject
{
    [ObservableProperty] private int _channelNumber;
    [ObservableProperty] private int _signalStrength;
    [ObservableProperty] private int _symbolQuality;
    [ObservableProperty] private bool _hasLock;
}
