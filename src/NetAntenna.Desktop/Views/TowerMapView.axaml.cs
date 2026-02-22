using Avalonia.Controls;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Widgets.ScaleBar;
using NetAntenna.Desktop.ViewModels;
using System.Collections.Specialized;
using NetAntenna.Core.Models;
using NetTopologySuite.Geometries;

namespace NetAntenna.Desktop.Views;

public partial class TowerMapView : UserControl
{
    private readonly MemoryLayer _towerLayer;

    public TowerMapView()
    {
        InitializeComponent();

        TowerMap.Map?.Layers.Add(OpenStreetMap.CreateTileLayer());
        TowerMap.Map?.Widgets.Add(new ScaleBarWidget(TowerMap.Map) { TextAlignment = Mapsui.Widgets.Alignment.Center });

        _towerLayer = new MemoryLayer { Name = "Towers" };
        TowerMap.Map?.Layers.Add(_towerLayer);

        // Center map on the US to start
        var (x, y) = SphericalMercator.FromLonLat(-98.5795, 39.8283);
        TowerMap.Map?.Navigator?.CenterOnAndZoomTo(new MPoint(x, y), 5000);

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TowerMapViewModel vm)
        {
            vm.Towers.CollectionChanged += OnTowersChanged;
            DrawTowers(vm.Towers);
        }
    }

    private void OnTowersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is TowerMapViewModel vm)
        {
            DrawTowers(vm.Towers);
        }
    }

    private void DrawTowers(IEnumerable<FccTower> towers)
    {
        var features = new List<GeometryFeature>();

        foreach (var t in towers)
        {
            // Mapsui uses Spherical Mercator for OSM layers
            var (x, y) = SphericalMercator.FromLonLat(t.Longitude, t.Latitude);
            var feature = new GeometryFeature
            {
                Geometry = new Point(x, y)
            };

            // Style based on Signal Strength (if integrated later, for now uniform)
            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new Brush(new Color(255, 193, 7, 180)), // Amber outline
                Outline = new Pen { Color = Color.Black, Width = 2 },
                SymbolScale = 0.5
            });

            // Add callsign as a label below the pin
            feature.Styles.Add(new LabelStyle
            {
                Text = $"{t.CallSign}\nCh {t.TransmitChannel}",
                ForeColor = Color.White,
                BackColor = new Brush(Color.Black),
                Offset = new Offset(0, 16),
                Halo = new Pen(Color.Black, 2)
            });

            features.Add(feature);
        }

        _towerLayer.Features = features;
        TowerMap.Refresh();
    }
}
