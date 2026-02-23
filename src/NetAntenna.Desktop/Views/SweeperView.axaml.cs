using Avalonia.Controls;
using NetAntenna.Desktop.ViewModels;
using ScottPlot;
using ScottPlot.Avalonia;

namespace NetAntenna.Desktop.Views;

public partial class SweeperView : UserControl
{
    public SweeperView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        var chart = this.FindControl<AvaPlot>("TrendChart");
        if (chart == null) return;

        // Apply dark theme before handing off to ViewModel
        chart.Plot.FigureBackground.Color = Color.FromHex("#1E1E2E");
        chart.Plot.DataBackground.Color   = Color.FromHex("#181825");
        chart.Plot.Axes.Color(Color.FromHex("#6C7086"));
        chart.Plot.Grid.MajorLineColor    = Color.FromHex("#313244");
        chart.Refresh();

        if (DataContext is SweeperViewModel vm)
            vm.SetChartControl(chart);
    }
}
