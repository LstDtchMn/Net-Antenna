using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public partial class AimingAssistantViewModel : ViewModelBase
{
    private readonly IDatabaseService _db;
    private readonly IRfPredictionEngine _predictionEngine;

    [ObservableProperty] private double _userLatitude;
    [ObservableProperty] private double _userLongitude;
    [ObservableProperty] private string _recommendedBearingText = "N/A";
    [ObservableProperty] private double _recommendedBearingAngle;

    public ObservableCollection<TargetTower> TargetTowers { get; } = new();

    public AimingAssistantViewModel(IDatabaseService db, IRfPredictionEngine predictionEngine)
    {
        _db = db;
        _predictionEngine = predictionEngine;
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        // Load User Coordinates from Settings
        var latStr = await _db.GetSettingAsync("user_lat");
        var lonStr = await _db.GetSettingAsync("user_lng");
        
        if (double.TryParse(latStr, out var lat) && double.TryParse(lonStr, out var lon))
        {
            UserLatitude = lat;
            UserLongitude = lon;
        }
        else
        {
            // Default center of US
            UserLatitude = 39.8283;
            UserLongitude = -98.5795;
        }

        var fccTowers = await _db.GetAllFccTowersAsync();
        
        // Populate the UI list with calculated distance/bearing for each tower
        TargetTowers.Clear();
        foreach (var tower in fccTowers.OrderByDescending(t => t.ErpKw)) // Sort by power roughly
        {
            var distance = _predictionEngine.CalculateDistanceKm(UserLatitude, UserLongitude, tower.Latitude, tower.Longitude);
            var bearing = _predictionEngine.CalculateBearingDegrees(UserLatitude, UserLongitude, tower.Latitude, tower.Longitude);
            
            TargetTowers.Add(new TargetTower(tower)
            {
                DistanceKm = distance,
                BearingDegrees = bearing
            });
        }
    }

    [RelayCommand]
    private void CalculateOptimalBearing()
    {
        var selected = TargetTowers.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            RecommendedBearingText = "Select at least 1 channel";
            RecommendedBearingAngle = 0;
            return;
        }

        // Extremely simplified "optimal bearing": average the azimuths of selected targets.
        // A true RF engine would sum the vectors weighted by FSPL predicted power.
        double sumSin = 0;
        double sumCos = 0;

        foreach (var t in selected)
        {
            var rad = t.BearingDegrees * Math.PI / 180.0;
            sumSin += Math.Sin(rad);
            sumCos += Math.Cos(rad);
        }

        var avgRad = Math.Atan2(sumSin / selected.Count, sumCos / selected.Count);
        var avgDeg = (avgRad * 180.0 / Math.PI + 360) % 360;

        RecommendedBearingAngle = avgDeg;
        RecommendedBearingText = $"{Math.Round(avgDeg)}°";
    }
}

public partial class TargetTower : ObservableObject
{
    public FccTower Tower { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private double _distanceKm;
    [ObservableProperty] private double _bearingDegrees;

    public TargetTower(FccTower tower)
    {
        Tower = tower;
    }
}
