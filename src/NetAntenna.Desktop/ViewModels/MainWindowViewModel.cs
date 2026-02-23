using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDeviceDiscovery _discovery;
    private readonly IDatabaseService _database;
    private readonly DashboardViewModel _dashboardVm;
    private readonly ChannelManagerViewModel _channelManagerVm;
    private readonly TowerMapViewModel _towerMapVm;
    private readonly SpectrumOverviewViewModel _spectrumOverviewVm;
    private readonly SweeperViewModel _sweeperVm;
    private readonly AimingAssistantViewModel _aimingAssistantVm;
    private readonly LogsViewModel _logsVm;
    private readonly SettingsViewModel _settingsVm;

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _selectedNavItem = "Dashboard";

    [ObservableProperty]
    private List<HdHomeRunDevice> _devices = new();

    [ObservableProperty]
    private HdHomeRunDevice? _selectedDevice;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isScanning;

    public MainWindowViewModel(
        IDeviceDiscovery discovery,
        IDatabaseService database,
        DashboardViewModel dashboardVm,
        ChannelManagerViewModel channelManagerVm,
        TowerMapViewModel towerMapVm,
        SpectrumOverviewViewModel spectrumOverviewVm,
        SweeperViewModel sweeperVm,
        AimingAssistantViewModel aimingAssistantVm,
        LogsViewModel logsVm,
        SettingsViewModel settingsVm)
    {
        _discovery = discovery;
        _database = database;
        _dashboardVm = dashboardVm;
        _channelManagerVm = channelManagerVm;
        _towerMapVm = towerMapVm;
        _spectrumOverviewVm = spectrumOverviewVm;
        _sweeperVm = sweeperVm;
        _aimingAssistantVm = aimingAssistantVm;
        _logsVm = logsVm;
        _settingsVm = settingsVm;
        _currentPage = dashboardVm;

        // Start initial device discovery
        _ = DiscoverDevicesAsync();
    }

    [RelayCommand]
    private void NavigateTo(string page)
    {
        SelectedNavItem = page;
        CurrentPage = page switch
        {
            "Dashboard" => _dashboardVm,
            "Channels" => _channelManagerVm,
            "TowerMap" => _towerMapVm,
            "SpectrumOverview" => _spectrumOverviewVm,
            "Sweeper" => _sweeperVm,
            "AimingAssistant" => _aimingAssistantVm,
            "Logs" => _logsVm,
            "Settings" => _settingsVm,
            _ => CurrentPage
        };
    }

    [RelayCommand]
    private async Task QuickScan()
    {
        if (SelectedDevice is null) return;

        IsScanning = true;
        try
        {
            await _dashboardVm.StartQuickScanAsync(SelectedDevice);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task DiscoverDevicesAsync()
    {
        ConnectionStatus = "Scanning...";
        try
        {
            var discovered = await _discovery.DiscoverDevicesAsync(TimeSpan.FromSeconds(5));

            // Also load previously saved devices from DB
            var savedDevices = await _database.GetAllDevicesAsync();

            // Merge: prefer fresh discovered data, keep saved devices that weren't found
            var merged = new Dictionary<string, HdHomeRunDevice>();
            foreach (var d in savedDevices)
                merged[d.DeviceId] = d;
            foreach (var d in discovered)
            {
                merged[d.DeviceId] = d;
                await _database.UpsertDeviceAsync(d);
            }

            Devices = merged.Values.ToList();

            if (Devices.Count > 0)
            {
                SelectedDevice ??= Devices[0];
                ConnectionStatus = $"Connected ({Devices.Count} device{(Devices.Count > 1 ? "s" : "")})";
                IsConnected = true;

                // Notify child ViewModels of device selection
                await OnDeviceSelectedAsync();
            }
            else
            {
                ConnectionStatus = "No devices found";
                IsConnected = false;
            }
        }
        catch (Exception)
        {
            ConnectionStatus = "Discovery failed";
            IsConnected = false;
        }
    }

    partial void OnSelectedDeviceChanged(HdHomeRunDevice? value)
    {
        if (value is not null)
        {
            _ = OnDeviceSelectedAsync();
        }
    }

    private async Task OnDeviceSelectedAsync()
    {
        if (SelectedDevice is null) return;

        await _dashboardVm.SetDeviceAsync(SelectedDevice);
        await _channelManagerVm.SetDeviceAsync(SelectedDevice);
        _sweeperVm.SetDevice(SelectedDevice);
    }
}
