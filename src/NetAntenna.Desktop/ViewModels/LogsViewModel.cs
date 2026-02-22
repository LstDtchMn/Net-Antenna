using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public partial class LogsViewModel : ViewModelBase
{
    private readonly IMemoryLogSink _logSink;
    
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private bool _clearOnStartup = false;
    
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public LogsViewModel(IMemoryLogSink logSink)
    {
        _logSink = logSink;
        _logSink.OnLogReceived += OnLogReceived;
        
        // Load initial logs
        foreach (var log in _logSink.GetRecentLogs(500))
        {
            LogEntries.Add(log);
        }
    }

    private void OnLogReceived(object? sender, LogEntry e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!MatchesFilter(e)) return;

            LogEntries.Add(e);
            
            // Keep UI collection manageable
            if (LogEntries.Count > 1000)
                LogEntries.RemoveAt(0);
        });
    }

    private bool MatchesFilter(LogEntry e)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return e.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               e.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               e.Level.ToString().Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
        _logSink.Clear();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task CopyLogsAsync()
    {
        var text = string.Join(Environment.NewLine,
            LogEntries.Select(e => $"[{e.Timestamp:HH:mm:ss.fff}] [{e.Level,-11}] {e.Category}: {e.Message}"));
        
        var clipboard = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Clipboard
            : null;

        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }

    partial void OnClearOnStartupChanged(bool value)
    {
        if (value)
        {
            LogEntries.Clear();
            _logSink.Clear();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        LogEntries.Clear();
        foreach (var log in _logSink.GetRecentLogs(1000))
        {
            if (MatchesFilter(log))
                LogEntries.Add(log);
        }
    }
}
