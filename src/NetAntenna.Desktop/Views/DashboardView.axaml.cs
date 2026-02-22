using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NetAntenna.Core.Models;
using NetAntenna.Desktop.ViewModels;
using ScottPlot;
using ScottPlot.Avalonia;

namespace NetAntenna.Desktop.Views;

public partial class DashboardView : UserControl
{
    private readonly List<double> _ssData = new();
    private readonly List<double> _snqData = new();
    private readonly List<double> _seqData = new();
    private readonly List<double> _timestamps = new();

    public DashboardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            SetupChart();
            vm.ChartSamples.CollectionChanged += OnChartSamplesChanged;
        }
    }

    private void SetupChart()
    {
        var plot = SignalChart.Plot;
        plot.Clear();

        // Dark theme styling
        plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E2E");
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#181825");
        plot.Axes.Color(ScottPlot.Color.FromHex("#B0B0B0"));
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#313244");
        plot.Grid.MinorLineColor = ScottPlot.Color.FromHex("#252535");

        // Axis labels
        plot.YLabel("Signal (%)");
        plot.XLabel("Time");
        plot.Axes.SetLimitsY(0, 105);
        plot.Title("Signal Quality Over Time");

        // Legend
        plot.Legend.IsVisible = true;
        plot.Legend.Alignment = Alignment.UpperRight;
        plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#1E1E2E");
        plot.Legend.FontColor = ScottPlot.Color.FromHex("#B0B0B0");
        plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#313244");

        SignalChart.Refresh();
    }

    private void OnChartSamplesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _ssData.Clear();
            _snqData.Clear();
            _seqData.Clear();
            _timestamps.Clear();
            SetupChart();
            return;
        }

        if (e.NewItems is null) return;

        foreach (SignalSample sample in e.NewItems)
        {
            _ssData.Add(sample.Ss);
            _snqData.Add(sample.Snq);
            _seqData.Add(sample.Seq);
            _timestamps.Add(sample.TimestampUnixMs / 1000.0); // Convert to seconds
        }

        RebuildPlots();
    }

    private void RebuildPlots()
    {
        var plot = SignalChart.Plot;
        plot.Clear();

        // Dark theme styling (reapply after clear)
        plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E2E");
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#181825");
        plot.Axes.Color(ScottPlot.Color.FromHex("#B0B0B0"));
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#313244");

        if (_ssData.Count > 1)
        {
            var ssSignal = plot.Add.Signal(_ssData.ToArray());
            ssSignal.LegendText = "SS";
            ssSignal.Color = ScottPlot.Color.FromHex("#4CAF50");
            ssSignal.LineWidth = 2;

            var snqSignal = plot.Add.Signal(_snqData.ToArray());
            snqSignal.LegendText = "SNQ";
            snqSignal.Color = ScottPlot.Color.FromHex("#00BCD4");
            snqSignal.LineWidth = 2;

            var seqSignal = plot.Add.Signal(_seqData.ToArray());
            seqSignal.LegendText = "SEQ";
            seqSignal.Color = ScottPlot.Color.FromHex("#FFC107");
            seqSignal.LineWidth = 2;

            plot.Axes.SetLimitsY(0, 105);
            plot.Axes.AutoScaleX();
        }
        else
        {
            plot.Title("Waiting for signal data...");
        }

        plot.Legend.IsVisible = true;
        plot.Legend.Alignment = Alignment.UpperRight;
        plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#1E1E2E");
        plot.Legend.FontColor = ScottPlot.Color.FromHex("#B0B0B0");

        SignalChart.Refresh();
    }

    private void OnTimeWindowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string window && DataContext is DashboardViewModel vm)
        {
            vm.SelectedTimeWindow = window;
        }
    }
}
