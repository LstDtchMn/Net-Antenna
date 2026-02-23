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
    [ObservableProperty] private bool _isLoadingSuggestions;
    [ObservableProperty] private bool _showSuggestions;
    [ObservableProperty] private ObservableCollection<GeocodingSuggestion> _suggestions = new();
    [ObservableProperty] private string _fccDownloadProgress = "";

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
        Towers = new ObservableCollection<FccTower>(towers);
        TowerCount = towers.Count;

        var lastUpdate = await _fccService.GetLastUpdateDateAsync();
        LastUpdateText = lastUpdate?.ToString("g") ?? "Never";
        // Load user coordinates and last address label
        UserLat = await _db.GetSettingAsync("user_lat") ?? "39.8283";
        UserLng = await _db.GetSettingAsync("user_lng") ?? "-98.5795";
        var savedAddress = await _db.GetSettingAsync("user_address");
        if (!string.IsNullOrWhiteSpace(savedAddress))
            SearchAddress = savedAddress;
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
