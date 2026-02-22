using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetAntenna.Core.Models;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public partial class TowerMapViewModel : ViewModelBase
{
    private readonly IFccDataService _fccService;
    private readonly NetAntenna.Core.Data.IDatabaseService _db;
    private readonly IGeocodingService _geocoding;

    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private string _lastUpdateText = "Never";
    [ObservableProperty] private int _towerCount;
    [ObservableProperty] private ObservableCollection<FccTower> _towers = new();
    
    [ObservableProperty] private string _userLat = "";
    [ObservableProperty] private string _userLng = "";
    
    [ObservableProperty] private string _searchAddress = "";
    [ObservableProperty] private bool _isSearchingAddress;

    public TowerMapViewModel(IFccDataService fccService, NetAntenna.Core.Data.IDatabaseService db, IGeocodingService geocoding)
    {
        _fccService = fccService;
        _db = db;
        _geocoding = geocoding;
        _ = LoadTowersAsync();
    }

    private async Task LoadTowersAsync()
    {
        var towers = await _db.GetAllFccTowersAsync();
        Towers = new ObservableCollection<FccTower>(towers);
        TowerCount = towers.Count;

        var lastUpdate = await _fccService.GetLastUpdateDateAsync();
        LastUpdateText = lastUpdate?.ToString("g") ?? "Never";
        
        // Load user coordinates
        UserLat = await _db.GetSettingAsync("user_lat") ?? "39.8283"; // Default US Center
        UserLng = await _db.GetSettingAsync("user_lng") ?? "-98.5795";
    }

    [RelayCommand]
    private async Task SaveLocationAsync()
    {
        if (double.TryParse(UserLat, out var lat) && double.TryParse(UserLng, out var lng))
        {
            await _db.SetSettingAsync("user_lat", lat.ToString());
            await _db.SetSettingAsync("user_lng", lng.ToString());
            // Map refresh will happen via MapControl logic monitoring this VM, or user can push a button
        }
    }

    [RelayCommand]
    private async Task SearchAddressAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchAddress) || IsSearchingAddress) return;

        IsSearchingAddress = true;
        try
        {
            var coords = await _geocoding.GetCoordinatesAsync(SearchAddress);
            if (coords.HasValue)
            {
                UserLat = coords.Value.Latitude.ToString("F4");
                UserLng = coords.Value.Longitude.ToString("F4");
                await SaveLocationAsync();
            }
        }
        finally
        {
            IsSearchingAddress = false;
        }
    }

    [RelayCommand]
    private async Task DownloadFccDataAsync()
    {
        if (IsUpdating) return;
        
        IsUpdating = true;
        try
        {
            await _fccService.DownloadAndIndexLmsDataAsync();
            await LoadTowersAsync(); // Refresh from DB
            LastUpdateText = DateTimeOffset.Now.ToString("g");
        }
        catch (Exception ex)
        {
            LastUpdateText = $"Error: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
