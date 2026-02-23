using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetAntenna.Core.Models;
using NetAntenna.Core.Services;
using NetAntenna.Core.Data;
using ScottPlot;
using ScottPlot.Avalonia;

namespace NetAntenna.Desktop.ViewModels;

public sealed class SelectableTuner : ObservableObject
{
    public int Index { get; init; }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public partial class SweeperViewModel : ViewModelBase, IDisposable
{
    private readonly ITunerSweeperService _sweeper;
    private readonly IDatabaseService _db;
    private HdHomeRunDevice? _currentDevice;

    // --- Tuner selection ---
    public ObservableCollection<SelectableTuner> AvailableTuners { get; } = new();

    // --- Results grid ---
    public ObservableCollection<ChannelStatistics> LiveResults { get; } = new();

    // --- Sweep progress ---
    [ObservableProperty] private bool _isSweeping;
    [ObservableProperty] private int _totalChannels;
    [ObservableProperty] private int _channelsScanned;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _runProgressText = "—";

    // --- Sweep profile settings ---
    [ObservableProperty] private decimal _dwellTimeSeconds = 15;
    [ObservableProperty] private decimal _runCount = 3;
    [ObservableProperty] private decimal _timeLimitMinutes = 10;

    // Sweep mode — one of these will be true at a time
    private SweepMode _selectedMode = SweepMode.FixedCount;
    public bool ModeFixedCount
    {
        get => _selectedMode == SweepMode.FixedCount;
        set { if (value) { _selectedMode = SweepMode.FixedCount; OnPropertyChanged(); OnPropertyChanged(nameof(ModeTimeLimited)); OnPropertyChanged(nameof(ModeIndefinite)); } }
    }
    public bool ModeTimeLimited
    {
        get => _selectedMode == SweepMode.TimeLimited;
        set { if (value) { _selectedMode = SweepMode.TimeLimited; OnPropertyChanged(); OnPropertyChanged(nameof(ModeFixedCount)); OnPropertyChanged(nameof(ModeIndefinite)); } }
    }
    public bool ModeIndefinite
    {
        get => _selectedMode == SweepMode.Indefinite;
        set { if (value) { _selectedMode = SweepMode.Indefinite; OnPropertyChanged(); OnPropertyChanged(nameof(ModeFixedCount)); OnPropertyChanged(nameof(ModeTimeLimited)); } }
    }

    public int SelectedModeIndex
    {
        get
        {
            if (ModeFixedCount) return 0;
            if (ModeTimeLimited) return 1;
            return 2;
        }
        set
        {
            if (value == 0) ModeFixedCount = true;
            else if (value == 1) ModeTimeLimited = true;
            else if (value == 2) ModeIndefinite = true;
            OnPropertyChanged();
        }
    }

    // --- Sweep History ---
    [ObservableProperty] private bool _isHistoryOpen;
    public ObservableCollection<SweepSessionViewModel> HistorySessions { get; } = new();
    [ObservableProperty] private SweepSessionViewModel? _selectedHistorySession;

    // --- Channel detail / chart ---
    [ObservableProperty] private ChannelStatistics? _selectedChannel;
    private AvaPlot? _chartControl;

    public SweeperViewModel(ITunerSweeperService sweeper, IDatabaseService db)
    {
        _sweeper = sweeper;
        _db = db;
        _sweeper.StatusChanged += OnSweeperStatusChanged;
        UpdateFromStatus(_sweeper.CurrentStatus);
    }

    public void SetDevice(HdHomeRunDevice device)
    {
        _currentDevice = device;
        foreach (var tuner in AvailableTuners)
            tuner.PropertyChanged -= Tuner_PropertyChanged;
            
        AvailableTuners.Clear();
        for (int i = 0; i < device.TunerCount; i++)
        {
            var tuner = new SelectableTuner { Index = i, IsSelected = false };
            tuner.PropertyChanged += Tuner_PropertyChanged;
            AvailableTuners.Add(tuner);
        }
        UpdateTunerWarnings();
    }

    private void Tuner_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableTuner.IsSelected))
        {
            IsAllTunersWarningDismissed = false;
            IsSomeTunersWarningDismissed = false;
            UpdateTunerWarnings();
        }
    }

    [ObservableProperty] private bool _isAllTunersWarningDismissed;
    [ObservableProperty] private bool _isSomeTunersWarningDismissed;

    private void UpdateTunerWarnings()
    {
        OnPropertyChanged(nameof(HasSelectedTuners));
        OnPropertyChanged(nameof(AllTunersSelected));
        OnPropertyChanged(nameof(HasSelectedTunersButNotAll));
        OnPropertyChanged(nameof(ShowAllTunersWarning));
        OnPropertyChanged(nameof(ShowSomeTunersWarning));
    }

    [RelayCommand]
    private void DismissAllTunersWarning()
    {
        IsAllTunersWarningDismissed = true;
        UpdateTunerWarnings();
    }

    [RelayCommand]
    private void DismissSomeTunersWarning()
    {
        IsSomeTunersWarningDismissed = true;
        UpdateTunerWarnings();
    }

    public bool HasSelectedTuners => AvailableTuners.Any(t => t.IsSelected);
    public bool AllTunersSelected => AvailableTuners.Count > 0 && AvailableTuners.All(t => t.IsSelected);
    public bool HasSelectedTunersButNotAll => HasSelectedTuners && !AllTunersSelected;

    public bool ShowAllTunersWarning => AllTunersSelected && !IsAllTunersWarningDismissed;
    public bool ShowSomeTunersWarning => HasSelectedTunersButNotAll && !IsSomeTunersWarningDismissed;

    /// <summary>Called from code-behind to wire the ScottPlot control.</summary>
    public void SetChartControl(AvaPlot chart) => _chartControl = chart;

    [RelayCommand]
    private async Task StartSweepAsync()
    {
        if (_currentDevice == null) return;

        var selectedTuners = AvailableTuners
            .Where(t => t.IsSelected)
            .Select(t => t.Index)
            .ToList();

        if (selectedTuners.Count == 0) return;

        var profile = new SweepProfile
        {
            Mode = _selectedMode,
            MaxRuns = (int)RunCount,
            MaxDuration = TimeSpan.FromMinutes((int)TimeLimitMinutes)
        };

        await _sweeper.StartSweepAsync(
            _currentDevice.DeviceId,
            selectedTuners,
            TimeSpan.FromSeconds((int)DwellTimeSeconds),
            profile);
    }

    [RelayCommand]
    private void StopSweep() => _sweeper.CancelSweep();

    [RelayCommand]
    private async Task ToggleHistoryCommand()
    {
        IsHistoryOpen = !IsHistoryOpen;
        if (IsHistoryOpen && _currentDevice != null)
        {
            var sessions = await _db.GetSweepSessionsAsync(_currentDevice.DeviceId);
            
            Dispatcher.UIThread.Post(() =>
            {
                HistorySessions.Clear();
                foreach (var session in sessions)
                {
                    HistorySessions.Add(new SweepSessionViewModel(session));
                }
            });
        }
    }

    [RelayCommand]
    private async Task LoadSelectedSessionCommand()
    {
        if (SelectedHistorySession == null || _currentDevice == null) return;
        
        IsHistoryOpen = false;
        
        // Load the session snapshot into the UI as if it's currently running
        var stats = await _db.GetChannelStatisticsAsync(
            _currentDevice.DeviceId, 
            SelectedHistorySession.Model.StartTimeUnixMs, 
            SelectedHistorySession.Model.EndTimeUnixMs);

        LiveResults.Clear();
        foreach (var stat in stats)
            LiveResults.Add(stat);

        IsSweeping = false;
        RunProgressText = $"Historical Session: {SelectedHistorySession.StartTimeFormatted}";
        ChannelsScanned = stats.Count;
        TotalChannels = stats.Count;
        ProgressPercent = 100;
        
        // We clear the chart since we don't save per-run discrete history, just the summary
        if (_chartControl != null)
        {
            var plot = _chartControl.Plot;
            plot.Clear();
            plot.Title("History View: Select channel to see overall stats");
            _chartControl.Refresh();
        }
    }

    private void OnSweeperStatusChanged(object? sender, SweeperStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateFromStatus(status);
            if (SelectedChannel != null)
                RefreshChart(status.CompletedRuns, SelectedChannel.Channel);
        });
    }

    private void UpdateFromStatus(SweeperStatus status)
    {
        IsSweeping = status.IsSweeping;
        TotalChannels = status.TotalChannels;
        ChannelsScanned = status.ChannelsScanned;
        ProgressPercent = TotalChannels > 0 ? (double)ChannelsScanned / TotalChannels * 100 : 0;

        // Run progress label
        RunProgressText = status.TargetRuns.HasValue
            ? $"Run {status.CurrentRun} / {status.TargetRuns}"
            : status.CurrentRun > 0
                ? $"Run {status.CurrentRun}"
                : "—";

        LiveResults.Clear();
        foreach (var r in status.LiveResults)
            LiveResults.Add(r);
    }

    partial void OnSelectedChannelChanged(ChannelStatistics? value)
    {
        if (value != null)
            RefreshChart(_sweeper.CurrentStatus.CompletedRuns, value.Channel);
    }

    private void RefreshChart(IReadOnlyList<SweepRunResult> runs, string channel)
    {
        if (_chartControl == null || runs.Count == 0) return;

        var plot = _chartControl.Plot;
        plot.Clear();
        plot.FigureBackground.Color = Color.FromHex("#11111B");
        plot.DataBackground.Color = Color.FromHex("#1E1E2E");

        var ssPoints  = new List<Coordinates>();
        var snqPoints = new List<Coordinates>();
        var seqPoints = new List<Coordinates>();

        foreach (var run in runs)
        {
            var ch = run.Results.FirstOrDefault(r => r.Channel == channel);
            if (ch == null) continue;

            ssPoints .Add(new Coordinates(run.RunNumber, ch.AvgSs));
            snqPoints.Add(new Coordinates(run.RunNumber, ch.AvgSnq));
            seqPoints.Add(new Coordinates(run.RunNumber, ch.AvgSeq));
        }

        if (ssPoints.Count > 0)
        {
            var ss  = plot.Add.ScatterPoints(ssPoints.Select(p => p.X).ToArray(),  ssPoints.Select(p => p.Y).ToArray());
            var snq = plot.Add.ScatterPoints(snqPoints.Select(p => p.X).ToArray(), snqPoints.Select(p => p.Y).ToArray());
            var seq = plot.Add.ScatterPoints(seqPoints.Select(p => p.X).ToArray(), seqPoints.Select(p => p.Y).ToArray());

            ss.LegendText  = "SS";
            snq.LegendText = "SNQ";
            seq.LegendText = "SEQ";

            ss.Color  = Color.FromHex("#00BCD4");
            snq.Color = Color.FromHex("#4CAF50");
            seq.Color = Color.FromHex("#FF9800");

            plot.Title($"Ch {channel} Signal Trend");
            plot.XLabel("Sweep Run #");
            plot.YLabel("Signal %");
            plot.Axes.SetLimitsY(0, 110);
            plot.ShowLegend();
        }

        _chartControl.Refresh();
    }

    public void Dispose() => _sweeper.StatusChanged -= OnSweeperStatusChanged;
}

public sealed class SweepSessionViewModel
{
    public SweepSession Model { get; }

    public string StartTimeFormatted => DateTimeOffset.FromUnixTimeMilliseconds(Model.StartTimeUnixMs)
                                                      .ToLocalTime()
                                                      .ToString("MMM dd, yyyy h:mm tt");
    public string Mode => string.IsNullOrWhiteSpace(Model.Mode) ? "Sweep" : Model.Mode;
    public int CompletedRuns => Model.CompletedRuns;
    
    public string DurationFormatted
    {
        get
        {
            var dur = TimeSpan.FromMilliseconds(Model.EndTimeUnixMs - Model.StartTimeUnixMs);
            if (dur.TotalHours >= 1)
                return $"{(int)dur.TotalHours}h {dur.Minutes}m";
            return $"{(int)dur.TotalMinutes}m {dur.Seconds}s";
        }
    }

    public SweepSessionViewModel(SweepSession model) => Model = model;
}
