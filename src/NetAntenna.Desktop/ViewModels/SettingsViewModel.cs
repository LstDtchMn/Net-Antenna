using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDeviceDiscovery _discovery;
    private readonly IDatabaseService _database;

    [ObservableProperty] private List<HdHomeRunDevice> _devices = new();
    [ObservableProperty] private string _manualIpAddress = string.Empty;
    [ObservableProperty] private int _pollingIntervalSec = 5;
    [ObservableProperty] private int _dataRetentionDays = 30;
    [ObservableProperty] private int _seqThresholdWeak = 50;
    [ObservableProperty] private int _seqThresholdGood = 80;
    [ObservableProperty] private string _antennaType = "Unknown";
    [ObservableProperty] private string _antennaHeightFt = "";
    [ObservableProperty] private string _appVersion = "1.0.0-alpha";
    [ObservableProperty] private string _statusMessage = "";

    public List<string> AntennaTypes { get; } = new()
    {
        "Unknown",
        "Indoor Flat",
        "Indoor Amplified",
        "Attic Mount - Omnidirectional",
        "Attic Mount - Directional",
        "Outdoor - Omnidirectional VHF/UHF",
        "Outdoor - Directional UHF",
        "Outdoor - Directional VHF/UHF",
        "Outdoor - Yagi"
    };

    public SettingsViewModel(IDeviceDiscovery discovery, IDatabaseService database)
    {
        _discovery = discovery;
        _database = database;
        _ = LoadSettingsAsync();
    }

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        var settings = await _database.GetAllSettingsAsync();

        if (settings.TryGetValue("polling_interval_sec", out var interval))
            PollingIntervalSec = int.TryParse(interval, out var i) ? i : 5;
        if (settings.TryGetValue("data_retention_days", out var retention))
            DataRetentionDays = int.TryParse(retention, out var r) ? r : 30;
        if (settings.TryGetValue("seq_threshold_weak", out var weak))
            SeqThresholdWeak = int.TryParse(weak, out var w) ? w : 50;
        if (settings.TryGetValue("seq_threshold_good", out var good))
            SeqThresholdGood = int.TryParse(good, out var g) ? g : 80;
        if (settings.TryGetValue("antenna_type", out var aType))
            AntennaType = aType;
        if (settings.TryGetValue("antenna_height_ft", out var aHeight))
            AntennaHeightFt = aHeight;

        Devices = (await _database.GetAllDevicesAsync()).ToList();
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        await _database.SetSettingAsync("polling_interval_sec", PollingIntervalSec.ToString());
        await _database.SetSettingAsync("data_retention_days", DataRetentionDays.ToString());
        await _database.SetSettingAsync("seq_threshold_weak", SeqThresholdWeak.ToString());
        await _database.SetSettingAsync("seq_threshold_good", SeqThresholdGood.ToString());
        await _database.SetSettingAsync("antenna_type", AntennaType);
        await _database.SetSettingAsync("antenna_height_ft", AntennaHeightFt);

        StatusMessage = "Settings saved.";
    }

    [RelayCommand]
    private async Task RescanDevices()
    {
        StatusMessage = "Scanning for devices...";
        try
        {
            var discovered = await _discovery.DiscoverDevicesAsync(TimeSpan.FromSeconds(5));
            foreach (var d in discovered)
                await _database.UpsertDeviceAsync(d);

            Devices = (await _database.GetAllDevicesAsync()).ToList();
            StatusMessage = $"Found {discovered.Count} device(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddManualDevice()
    {
        if (string.IsNullOrWhiteSpace(ManualIpAddress))
        {
            StatusMessage = "Please enter an IP address.";
            return;
        }

        StatusMessage = $"Connecting to {ManualIpAddress}...";
        var device = await _discovery.GetDeviceByIpAsync(ManualIpAddress);
        if (device is not null)
        {
            await _database.UpsertDeviceAsync(device);
            Devices = (await _database.GetAllDevicesAsync()).ToList();
            StatusMessage = $"Added {device.FriendlyName} ({device.DeviceId}).";
            ManualIpAddress = string.Empty;
        }
        else
        {
            StatusMessage = $"No HDHomeRun found at {ManualIpAddress}.";
        }
    }

    [RelayCommand]
    private async Task PurgeOldData()
    {
        await _database.PurgeOldSamplesAsync(DataRetentionDays);
        StatusMessage = $"Purged signal data older than {DataRetentionDays} days.";
    }
}
