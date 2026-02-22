using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetAntenna.Core.Services;

namespace NetAntenna.Desktop.ViewModels;

public partial class LogsViewModel : ViewModelBase
{
    private readonly IMemoryLogSink _logSink;
    
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _autoScroll = true;
    
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
            if (string.IsNullOrWhiteSpace(SearchText) || 
                e.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                e.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                LogEntries.Add(e);
                
                // Keep UI collection manageable
                if (LogEntries.Count > 1000)
                {
                    LogEntries.RemoveAt(0);
                }
            }
        });
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
    }
    
    partial void OnSearchTextChanged(string value)
    {
        LogEntries.Clear();
        foreach (var log in _logSink.GetRecentLogs(1000))
        {
            if (string.IsNullOrWhiteSpace(value) || 
                log.Message.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                log.Category.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                LogEntries.Add(log);
            }
        }
    }
}
