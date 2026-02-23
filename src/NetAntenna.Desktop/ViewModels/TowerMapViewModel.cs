using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetAntenna.Core.Models;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public enum MapLayer { Street, Terrain, Satellite }

public partial class TowerMapViewModel : ViewModelBase
{
    private readonly IFccDataService _fccService;
    private readonly NetAntenna.Core.Data.IDatabaseService _db;
    private readonly IGeocodingService _geocoding;

    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private string _lastUpdateText = "Never";
    [ObservableProperty] private int _towerCount;
    [ObservableProperty] private ObservableCollection<FccTower> _towers = new();
    private List<FccTower> _allTowers = new();

    [ObservableProperty] private ObservableCollection<string> _radiusOptions = new();
    [ObservableProperty] private string _selectedRadius = "70 miles";
    
    [ObservableProperty] private string _userLat = "";
    [ObservableProperty] private string _userLng = "";
    
    [ObservableProperty] private string _searchAddress = "";
    [ObservableProperty] private bool _isSearchingAddress;
    [ObservableProperty] private bool _isLoadingSuggestions;
    [ObservableProperty] private bool _showSuggestions;
    [ObservableProperty] private ObservableCollection<GeocodingSuggestion> _suggestions = new();
    [ObservableProperty] private string _fccDownloadProgress = "";
    [ObservableProperty] private MapLayer _selectedMapLayer = MapLayer.Street;
    [ObservableProperty] private bool _zoomToLocationRequested;  // toggled to notify view
    [ObservableProperty] private bool _zoomInToMeRequested;      // toggled to notify view
    [ObservableProperty] private bool _zoomOutFromMeRequested;   // toggled to notify view
    [ObservableProperty] private bool _resetMapRequested;        // toggled to notify view

    [RelayCommand]
    private void SetMapLayer(MapLayer layer) => SelectedMapLayer = layer;

    [RelayCommand]
    private void ZoomToLocation()
    {
        // Toggle the bool to trigger a property-changed notification the view can react to
        ZoomToLocationRequested = !ZoomToLocationRequested;
    }

    [RelayCommand]
    private void ZoomInToMe()
    {
        ZoomInToMeRequested = !ZoomInToMeRequested;
    }

    [RelayCommand]
    private void ZoomOutFromMe()
    {
        ZoomOutFromMeRequested = !ZoomOutFromMeRequested;
    }

    [RelayCommand]
    private void ResetMap()
    {
        ResetMapRequested = !ResetMapRequested;
    }

    [RelayCommand]
    private void ClearTowers()
    {
        Towers.Clear();
        TowerCount = 0;
    }

    private CancellationTokenSource? _debounceCts;

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
        _allTowers = towers.ToList();

        var lastUpdate = await _fccService.GetLastUpdateDateAsync();
        LastUpdateText = lastUpdate?.ToString("g") ?? "Never";
        // Load user coordinates and last address label
        UserLat = await _db.GetSettingAsync("user_lat") ?? "39.8283";
        UserLng = await _db.GetSettingAsync("user_lng") ?? "-98.5795";
        var savedAddress = await _db.GetSettingAsync("user_address");
        if (!string.IsNullOrWhiteSpace(savedAddress))
        {
            SearchAddress = savedAddress;
            _debounceCts?.Cancel();
            ShowSuggestions = false;
        }

        // Populate radius options and integrate "My Antenna" if set
        var options = new List<string> { "15 miles", "35 miles", "50 miles", "70 miles", "85 miles", "100 miles", "150 miles", "All Towers" };
        var antennaType = await _db.GetSettingAsync("antenna_type");
        if (!string.IsNullOrWhiteSpace(antennaType) && antennaType != "Unknown")
        {
            int estimatedMiles = antennaType switch
            {
                "Indoor Flat" => 25,
                "Indoor Amplified" => 35,
                "Attic Mount - Omnidirectional" => 50,
                "Attic Mount - Directional" => 60,
                "Outdoor - Omnidirectional VHF/UHF" => 50,
                "Outdoor - Directional UHF" => 70,
                "Outdoor - Directional VHF/UHF" => 70,
                "Outdoor - Yagi" => 100,
                _ => 50
            };
            options.Insert(0, $"My Antenna (~{estimatedMiles} mi)");
        }

        RadiusOptions = new ObservableCollection<string>(options);
        if (string.IsNullOrEmpty(SelectedRadius) || !RadiusOptions.Contains(SelectedRadius))
        {
            SelectedRadius = "70 miles";
        }
        else
        {
            FilterTowers(); // Trigger filter explicitly if we kept previous radius
        }
    }

    partial void OnSelectedRadiusChanged(string value)
    {
        FilterTowers();
    }

    partial void OnUserLatChanged(string value) => FilterTowers();
    partial void OnUserLngChanged(string value) => FilterTowers();

    private void FilterTowers()
    {
        if (_allTowers == null || _allTowers.Count == 0 || !double.TryParse(UserLat, out var lat) || !double.TryParse(UserLng, out var lng)) 
            return;

        if (SelectedRadius == "All Towers" || string.IsNullOrEmpty(SelectedRadius))
        {
            Towers = new ObservableCollection<FccTower>(_allTowers);
            TowerCount = _allTowers.Count;
            return;
        }

        // Parse distance from string like "35 miles" or "My Antenna (~85 mi)"
        double maxDist = double.MaxValue;
        var digits = new string(SelectedRadius.Where(char.IsDigit).ToArray());
        if (double.TryParse(digits, out var parsedDist))
        {
            maxDist = parsedDist;
        }

        var filtered = _allTowers.Where(t => CalculateDistanceMiles(lat, lng, t.Latitude, t.Longitude) <= maxDist).ToList();
        
        Towers = new ObservableCollection<FccTower>(filtered);
        TowerCount = filtered.Count;
    }

    private static double CalculateDistanceMiles(double lat1, double lon1, double lat2, double lon2)
    {
        var earthRadiusMiles = 3958.8;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMiles * c;
    }

    // Called automatically by CommunityToolkit whenever SearchAddress changes
    partial void OnSearchAddressChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;

        if (string.IsNullOrWhiteSpace(value) || value.Length < 3)
        {
            Suggestions.Clear();
            ShowSuggestions = false;
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, ct); // debounce 350ms
                
                Avalonia.Threading.Dispatcher.UIThread.Post(() => IsLoadingSuggestions = true);
                
                var results = await _geocoding.GetSuggestionsAsync(value, ct);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Suggestions.Clear();
                    foreach (var s in results) Suggestions.Add(s);
                    ShowSuggestions = Suggestions.Count > 0;
                    IsLoadingSuggestions = false;
                });
            }
            catch (OperationCanceledException) { /* debounced away */ }
        }, ct);
    }

    [RelayCommand]
    private async Task SelectSuggestionAsync(GeocodingSuggestion suggestion)
    {
        ShowSuggestions = false;
        Suggestions.Clear();

        UserLat = suggestion.Latitude.ToString("F4");
        UserLng = suggestion.Longitude.ToString("F4");
        await SaveLocationAsync();

        // Update the search bar text AFTER saving so OnSearchAddressChanged
        // debounce fires on a saved value — store address label separately
        await _db.SetSettingAsync("user_address", suggestion.DisplayName);
        SearchAddress = suggestion.DisplayName;

        // Suppress the debounce suggestion re-trigger
        _debounceCts?.Cancel();
        ShowSuggestions = false;
    }

    [RelayCommand]
    private void DismissSuggestions()
    {
        ShowSuggestions = false;
    }

    [RelayCommand]
    private async Task SaveLocationAsync()
    {
        if (double.TryParse(UserLat, out var lat) && double.TryParse(UserLng, out var lng))
        {
            await _db.SetSettingAsync("user_lat", lat.ToString());
            await _db.SetSettingAsync("user_lng", lng.ToString());
        }
    }

    [RelayCommand]
    private async Task SearchAddressAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchAddress) || IsSearchingAddress) return;

        ShowSuggestions = false;
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
        FccDownloadProgress = "Connecting to FCC...";
        try
        {
            var progress = new Progress<int>(pct =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    FccDownloadProgress = pct switch
                    {
                        < 42  => $"⬇ Downloading ZIP... {pct}%",
                        < 50  => "📦 Decompressing...",
                        < 66  => "🏢 Parsing facilities...",
                        < 80  => "📋 Parsing applications...",
                        < 92  => "📡 Parsing tower data...",
                        < 100 => "💾 Saving to database...",
                        _     => "✅ Done"
                    }));
            
            await _fccService.DownloadAndIndexLmsDataAsync(progress);
            await LoadTowersAsync();
            FccDownloadProgress = $"✅ {TowerCount:N0} towers indexed";
        }
        catch (Exception ex)
        {
            FccDownloadProgress = $"❌ {ex.Message}";
            LastUpdateText = "Failed";
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
