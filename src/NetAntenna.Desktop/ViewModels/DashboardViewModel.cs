using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ITunerClient _tunerClient;
    private readonly ISignalLogger _signalLogger;
    private readonly IDatabaseService _database;
    private readonly INwsWeatherService _weatherService;

    private HdHomeRunDevice? _currentDevice;

    // --- Live signal tiles ---
    [ObservableProperty] private int _signalStrength;
    [ObservableProperty] private int _signalToNoiseQuality;
    [ObservableProperty] private int _symbolQuality;
    [ObservableProperty] private string _currentChannel = "—";
    [ObservableProperty] private string _lockType = "—";
    [ObservableProperty] private string _bitrate = "—";
    [ObservableProperty] private string _ssColor = "#757575";
    [ObservableProperty] private string _snqColor = "#757575";
    [ObservableProperty] private string _seqColor = "#757575";

    // --- Tuner selection ---
    [ObservableProperty] private int _selectedTunerIndex;
    [ObservableProperty] private List<int> _availableTuners = new();

    // --- Chart data ---
    [ObservableProperty] private bool _isLiveMode = true;
    [ObservableProperty] private string _selectedTimeWindow = "1h";
    [ObservableProperty] private bool _isLogging;
    [ObservableProperty] private string _currentWeather = "Weather condition unknown";

    // Signal data for the chart
    public ObservableCollection<SignalSample> ChartSamples { get; } = new();

    public DashboardViewModel(
        ITunerClient tunerClient,
        ISignalLogger signalLogger,
        IDatabaseService database,
        INwsWeatherService weatherService)
    {
        _tunerClient = tunerClient;
        _signalLogger = signalLogger;
        _database = database;
        _weatherService = weatherService;

        // Subscribe to real-time samples
        _signalLogger.SignalSampleReceived += OnSignalSampleReceived;
    }

    public async Task SetDeviceAsync(HdHomeRunDevice device)
    {
        _currentDevice = device;
        AvailableTuners = Enumerable.Range(0, device.TunerCount).ToList();
        SelectedTunerIndex = 0;

        // Load historical data for the chart
        await LoadHistoricalDataAsync();
        
        // Load weather
        _ = LoadWeatherAsync();
    }

    private async Task LoadWeatherAsync()
    {
        var latStr = await _database.GetSettingAsync("user_lat");
        var lonStr = await _database.GetSettingAsync("user_lng");
        
        if (double.TryParse(latStr, out var lat) && double.TryParse(lonStr, out var lon))
        {
            var weather = await _weatherService.GetCurrentConditionsAsync(lat, lon);
            if (!string.IsNullOrEmpty(weather))
            {
                CurrentWeather = $"Local Weather: {weather}";
            }
        }
        else
        {
            CurrentWeather = "Set location in settings for weather";
        }
    }

    public async Task StartQuickScanAsync(HdHomeRunDevice device)
    {
        if (_signalLogger.IsRunning)
            await _signalLogger.StopAsync();

        _currentDevice = device;
        IsLogging = true;
        await _signalLogger.StartAsync(
            device.DeviceId, device.BaseUrl, device.TunerCount,
            TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private async Task ToggleLogging()
    {
        if (_currentDevice is null) return;

        if (_signalLogger.IsRunning)
        {
            await _signalLogger.StopAsync();
            IsLogging = false;
        }
        else
        {
            var intervalStr = await _database.GetSettingAsync("polling_interval_sec") ?? "5";
            var interval = int.TryParse(intervalStr, out var sec) ? sec : 5;

            await _signalLogger.StartAsync(
                _currentDevice.DeviceId, _currentDevice.BaseUrl,
                _currentDevice.TunerCount, TimeSpan.FromSeconds(interval));
            IsLogging = true;
        }
    }

    [RelayCommand]
    private async Task RefreshTunerStatus()
    {
        if (_currentDevice is null) return;

        try
        {
            var status = await _tunerClient.GetTunerStatusAsync(
                _currentDevice.BaseUrl, SelectedTunerIndex);
            UpdateTiles(status);
        }
        catch (Exception)
        {
            // Device unreachable
            ResetTiles();
        }
    }

    [RelayCommand]
    private async Task LoadHistoricalDataAsync()
    {
        if (_currentDevice is null) return;

        var toMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var fromMs = SelectedTimeWindow switch
        {
            "1h" => toMs - 3_600_000L,
            "6h" => toMs - 21_600_000L,
            "24h" => toMs - 86_400_000L,
            "All" => 0L,
            _ => toMs - 3_600_000L
        };

        var samples = await _database.GetSamplesAsync(
            _currentDevice.DeviceId, fromMs, toMs, SelectedTunerIndex);

        ChartSamples.Clear();
        foreach (var s in samples)
            ChartSamples.Add(s);
    }

    partial void OnSelectedTunerIndexChanged(int value)
    {
        _ = RefreshTunerStatus();
        _ = LoadHistoricalDataAsync();
    }

    partial void OnSelectedTimeWindowChanged(string value)
    {
        _ = LoadHistoricalDataAsync();
    }

    private void OnSignalSampleReceived(object? sender, SignalSample sample)
    {
        if (_currentDevice is null || sample.DeviceId != _currentDevice.DeviceId)
            return;

        if (sample.TunerIndex == SelectedTunerIndex)
        {
            // Update tiles from the live sample
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                SignalStrength = sample.Ss;
                SignalToNoiseQuality = sample.Snq;
                SymbolQuality = sample.Seq;
                CurrentChannel = sample.Channel;
                LockType = sample.LockType;
                Bitrate = FormatBitrate(sample.Bps);
                SsColor = GetSignalColor(sample.Ss);
                SnqColor = GetSignalColor(sample.Snq);
                SeqColor = GetSignalColor(sample.Seq);

                if (IsLiveMode)
                {
                    ChartSamples.Add(sample);

                    // Trim old samples from the chart view to keep it performant
                    var cutoffMs = SelectedTimeWindow switch
                    {
                        "1h" => sample.TimestampUnixMs - 3_600_000L,
                        "6h" => sample.TimestampUnixMs - 21_600_000L,
                        "24h" => sample.TimestampUnixMs - 86_400_000L,
                        _ => 0L
                    };
                    while (ChartSamples.Count > 0 && ChartSamples[0].TimestampUnixMs < cutoffMs)
                        ChartSamples.RemoveAt(0);
                }
            });
        }
    }

    private void UpdateTiles(TunerStatus status)
    {
        SignalStrength = status.SignalStrength;
        SignalToNoiseQuality = status.SignalToNoiseQuality;
        SymbolQuality = status.SymbolQuality;
        CurrentChannel = status.Channel;
        LockType = status.Lock;
        Bitrate = FormatBitrate(status.BitsPerSecond);
        SsColor = GetSignalColor(status.SignalStrength);
        SnqColor = GetSignalColor(status.SignalToNoiseQuality);
        SeqColor = GetSignalColor(status.SymbolQuality);
    }

    private void ResetTiles()
    {
        SignalStrength = 0;
        SignalToNoiseQuality = 0;
        SymbolQuality = 0;
        CurrentChannel = "—";
        LockType = "—";
        Bitrate = "—";
        SsColor = SnqColor = SeqColor = "#757575";
    }

    private static string GetSignalColor(int value) => value switch
    {
        >= 80 => "#4CAF50",  // Green
        >= 50 => "#FFC107",  // Amber
        > 0 => "#F44336",    // Red
        _ => "#757575"       // Gray
    };

    private static string FormatBitrate(long bps) => bps switch
    {
        >= 1_000_000 => $"{bps / 1_000_000.0:F1} Mbps",
        >= 1_000 => $"{bps / 1_000.0:F1} Kbps",
        > 0 => $"{bps} bps",
        _ => "—"
    };
}
