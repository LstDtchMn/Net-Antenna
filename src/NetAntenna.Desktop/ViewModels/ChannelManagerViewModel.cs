using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetAntenna.Core.Data;
using NetAntenna.Core.Models;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public partial class ChannelManagerViewModel : ViewModelBase
{
    private readonly ITunerClient _tunerClient;
    private readonly IDatabaseService _database;

    private HdHomeRunDevice? _currentDevice;

    [ObservableProperty]
    private ObservableCollection<ChannelInfo> _channels = new();

    [ObservableProperty]
    private int _weakThreshold = 50;

    [ObservableProperty]
    private bool _isLoading;

    public ChannelManagerViewModel(ITunerClient tunerClient, IDatabaseService database)
    {
        _tunerClient = tunerClient;
        _database = database;
    }

    public async Task SetDeviceAsync(HdHomeRunDevice device)
    {
        _currentDevice = device;
        await LoadChannelsAsync();
    }

    [RelayCommand]
    private async Task LoadChannelsAsync()
    {
        if (_currentDevice is null) return;

        IsLoading = true;
        try
        {
            // Fetch fresh lineup from the device
            var lineup = await _tunerClient.GetLineupAsync(_currentDevice.BaseUrl);

            // Merge with saved local state (favorites/hidden)
            var savedChannels = await _database.GetChannelLineupAsync(_currentDevice.DeviceId);
            var savedMap = savedChannels.ToDictionary(c => c.GuideNumber);

            var merged = new List<ChannelInfo>();
            foreach (var ch in lineup)
            {
                if (savedMap.TryGetValue(ch.GuideNumber, out var saved))
                {
                    ch.IsFavorite = saved.IsFavorite;
                    ch.IsHidden = saved.IsHidden;
                    ch.LastSs = saved.LastSs;
                    ch.LastSnq = saved.LastSnq;
                    ch.LastSeq = saved.LastSeq;
                }
                merged.Add(ch);
            }

            // Save the merged lineup to DB
            await _database.UpsertChannelsAsync(_currentDevice.DeviceId, merged);

            Channels = new ObservableCollection<ChannelInfo>(merged);
        }
        catch (Exception)
        {
            // Load from DB if device is unreachable
            var saved = await _database.GetChannelLineupAsync(_currentDevice.DeviceId);
            Channels = new ObservableCollection<ChannelInfo>(saved);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFavorite(ChannelInfo channel)
    {
        if (_currentDevice is null) return;

        channel.IsFavorite = !channel.IsFavorite;

        try
        {
            await _tunerClient.SetChannelFavoriteAsync(
                _currentDevice.BaseUrl, channel.GuideNumber, channel.IsFavorite);
        }
        catch (Exception)
        {
            // Continue even if device POST fails — save locally
        }

        await _database.UpsertChannelAsync(_currentDevice.DeviceId, channel);
    }

    [RelayCommand]
    private async Task ToggleHidden(ChannelInfo channel)
    {
        if (_currentDevice is null) return;

        channel.IsHidden = !channel.IsHidden;

        try
        {
            await _tunerClient.SetChannelVisibilityAsync(
                _currentDevice.BaseUrl, channel.GuideNumber, !channel.IsHidden);
        }
        catch (Exception)
        {
            // Continue even if device POST fails — save locally
        }

        await _database.UpsertChannelAsync(_currentDevice.DeviceId, channel);
    }

    [RelayCommand]
    private async Task HideWeakChannels()
    {
        if (_currentDevice is null) return;

        var toHide = Channels.Where(c => c.LastSeq.HasValue && c.LastSeq.Value < WeakThreshold && !c.IsHidden).ToList();

        foreach (var ch in toHide)
        {
            ch.IsHidden = true;
            try
            {
                await _tunerClient.SetChannelVisibilityAsync(
                    _currentDevice.BaseUrl, ch.GuideNumber, false);
            }
            catch { /* continue */ }
            await _database.UpsertChannelAsync(_currentDevice.DeviceId, ch);
        }

        // Refresh the list to reflect changes
        await LoadChannelsAsync();
    }

    [RelayCommand]
    private async Task ExportChannelConfig()
    {
        if (_currentDevice is null) return;

        var channels = await _database.GetChannelLineupAsync(_currentDevice.DeviceId);
        var json = System.Text.Json.JsonSerializer.Serialize(channels,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"NetAntenna_Channels_{_currentDevice.DeviceId}.json");
        await File.WriteAllTextAsync(path, json);
    }
}
