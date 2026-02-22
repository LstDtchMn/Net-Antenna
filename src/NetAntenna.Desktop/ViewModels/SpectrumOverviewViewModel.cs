using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetAntenna.Core.Data;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public partial class SpectrumOverviewViewModel : ViewModelBase
{
    private readonly ITunerClient _tunerClient;
    private readonly IDatabaseService _db;

    [ObservableProperty] private bool _isSweeping;
    [ObservableProperty] private int _currentSweepChannel;
    [ObservableProperty] private int _progressPercent;
    
    // Channels 14 -> 36 are the modern post-repack UHF TV band
    public ObservableCollection<SpectrumChannel> Channels { get; } = new();

    public SpectrumOverviewViewModel(ITunerClient tunerClient, IDatabaseService db)
    {
        _tunerClient = tunerClient;
        _db = db;
        
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
        
        // Find a device to use
        var devices = await _db.GetAllDevicesAsync();
        var device = devices.FirstOrDefault();
        if (device == null) return;

        IsSweeping = true;
        ProgressPercent = 0;

        try
        {
            // Reset all channels
            foreach (var ch in Channels)
            {
                ch.SignalStrength = 0;
                ch.SymbolQuality = 0;
                ch.HasLock = false;
            }

            // Perform the sweep using Tuner 0
            for (int i = 0; i < Channels.Count; i++)
            {
                var ch = Channels[i];
                CurrentSweepChannel = ch.ChannelNumber;
                
                // Command tuner to tune to physical channel
                await _tunerClient.SetChannelAsync(device.BaseUrl, 0, $"8vsb:{ch.ChannelNumber}");
                
                // Wait 1 second for the tuner to acquire a lock
                await Task.Delay(1000);

                var status = await _tunerClient.GetTunerStatusAsync(device.BaseUrl, 0);
                if (status != null)
                {
                    ch.SignalStrength = status.SignalStrength;
                    ch.SymbolQuality = status.SymbolQuality;
                    ch.HasLock = status.Lock != "none";
                }

                ProgressPercent = (int)((i + 1) / (float)Channels.Count * 100);
            }
        }
        finally
        {
            // Release tuner
            if (device != null)
            {
                await _tunerClient.SetChannelAsync(device.BaseUrl, 0, "none");
            }
            IsSweeping = false;
            ProgressPercent = 100;
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
