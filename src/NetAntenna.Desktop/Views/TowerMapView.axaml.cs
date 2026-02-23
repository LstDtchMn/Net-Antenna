using Avalonia.Controls;
using BruTile.Predefined;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using Mapsui.Widgets.ScaleBar;
using NetAntenna.Desktop.ViewModels;
using System.Collections.Specialized;
using NetAntenna.Core.Models;
using NetTopologySuite.Geometries;

namespace NetAntenna.Desktop.Views;

public partial class TowerMapView : UserControl
{
    private readonly MemoryLayer _towerLayer;
    private TileLayer? _baseLayer;

    public TowerMapView()
    {
        InitializeComponent();

        _baseLayer = OpenStreetMap.CreateTileLayer();
        TowerMap.Map?.Layers.Add(_baseLayer);
        TowerMap.Map?.Widgets.Add(new ScaleBarWidget(TowerMap.Map) { TextAlignment = Mapsui.Widgets.Alignment.Center });

        _towerLayer = new MemoryLayer { Name = "Towers" };
        TowerMap.Map?.Layers.Add(_towerLayer);

        // Center map initially (will be overridden by DataContext if valid)
        var (x, y) = SphericalMercator.FromLonLat(-98.5795, 39.8283);
        TowerMap.Map?.Navigator?.CenterOnAndZoomTo(new MPoint(x, y), 100000);

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TowerMapViewModel vm)
        {
            vm.Towers.CollectionChanged += OnTowersChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            DrawTowers(vm.Towers);
            CenterMap(vm.UserLat, vm.UserLng);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (DataContext is TowerMapViewModel vm)
        {
            if (e.PropertyName == nameof(TowerMapViewModel.UserLat) ||
                e.PropertyName == nameof(TowerMapViewModel.UserLng))
            {
                CenterMap(vm.UserLat, vm.UserLng);
            }
            else if (e.PropertyName == nameof(TowerMapViewModel.SelectedMapLayer))
            {
                SwitchBaseLayer(vm.SelectedMapLayer);
            }
        }
    }

    private void SwitchBaseLayer(MapLayer layer)
    {
        if (TowerMap.Map is null) return;

        // Remove the current base layer
        if (_baseLayer is not null)
            TowerMap.Map.Layers.Remove(_baseLayer);

        _baseLayer = layer switch
        {
            MapLayer.Terrain   => new TileLayer(KnownTileSources.Create(KnownTileSource.EsriWorldTopo))  { Name = "Base" },
            MapLayer.Satellite => new TileLayer(KnownTileSources.Create(KnownTileSource.BingAerial)) { Name = "Base" },
            _                  => OpenStreetMap.CreateTileLayer(),
        };

        // Insert at index 0 so towers remain on top
        TowerMap.Map.Layers.Insert(0, _baseLayer);
        TowerMap.Refresh();
    }

    private void CenterMap(string latStr, string lngStr)
    {
        if (double.TryParse(latStr, out var lat) && double.TryParse(lngStr, out var lng))
        {
            var (x, y) = SphericalMercator.FromLonLat(lng, lat);
            TowerMap.Map?.Navigator?.CenterOnAndZoomTo(new MPoint(x, y), 5000); // Zoom level 5000 (roughly regional)
            
            // Re-draw towers whenever location changes so user pin is updated
            if (DataContext is TowerMapViewModel vm)
            {
                DrawTowers(vm.Towers);
            }
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

        // Add User Location Pin
        if (DataContext is TowerMapViewModel vm && 
            double.TryParse(vm.UserLat, out var uLat) && 
            double.TryParse(vm.UserLng, out var uLng))
        {
            var (ux, uy) = SphericalMercator.FromLonLat(uLng, uLat);
            var userFeature = new GeometryFeature
            {
                Geometry = new Point(ux, uy)
            };
            userFeature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new Brush(new Color(76, 175, 80, 200)), // Green
                Outline = new Pen { Color = Color.White, Width = 2 },
                SymbolScale = 0.6
            });
            userFeature.Styles.Add(new LabelStyle
            {
                Text = "My Location",
                ForeColor = Color.White,
                BackColor = new Brush(Color.Black),
                Offset = new Offset(0, 16),
                Halo = new Pen(Color.Black, 2)
            });
            features.Add(userFeature);
        }

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
