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

    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private string _lastUpdateText = "Never";
    [ObservableProperty] private int _towerCount;
    [ObservableProperty] private ObservableCollection<FccTower> _towers = new();

    public TowerMapViewModel(IFccDataService fccService, NetAntenna.Core.Data.IDatabaseService db)
    {
        _fccService = fccService;
        _db = db;
        _ = LoadTowersAsync();
    }

    private async Task LoadTowersAsync()
    {
        var towers = await _db.GetAllFccTowersAsync();
        Towers = new ObservableCollection<FccTower>(towers);
        TowerCount = towers.Count;

        var lastUpdate = await _fccService.GetLastUpdateDateAsync();
        LastUpdateText = lastUpdate?.ToString("g") ?? "Never";
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
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
