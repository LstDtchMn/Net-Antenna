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
using NetAntenna.Core.Services;
using NetTopologySuite.Geometries;

namespace NetAntenna.Desktop.Views;

public partial class TowerMapView : UserControl
{
    private readonly MemoryLayer _towerLayer;
    private readonly MemoryLayer _contourLayer;
    private TileLayer? _baseLayer;
    private readonly IRfPredictionEngine _rfEngine = new RfPredictionEngine(); // Lightweight math engine

    public TowerMapView()
    {
        InitializeComponent();

        // Ensure the Avalonia control is transparent so the Border's dark theme shows through
        TowerMap.Background = Avalonia.Media.Brushes.Transparent;

        // Center map initially (will be overridden by DataContext if valid)
        var (x, y) = SphericalMercator.FromLonLat(-98.5795, 39.8283);
        if (TowerMap.Map != null && TowerMap.Map.Navigator != null)
        {
            TowerMap.Map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), 100000);
        }

        _contourLayer = new MemoryLayer { Name = "Contours" };
        _towerLayer = new MemoryLayer { Name = "Towers" };

        DataContextChanged += OnDataContextChanged;
    }

    private bool _mapInitialized = false;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (!_mapInitialized && TowerMap.Map != null)
        {
            TowerMap.Map.CRS = "EPSG:3857";
            TowerMap.Map.BackColor = new Mapsui.Styles.Color(30, 30, 46); // Mapsui dark background

            TowerMap.Map.Widgets.Add(new ScaleBarWidget(TowerMap.Map) { TextAlignment = Mapsui.Widgets.Alignment.Center });

            // Initialize layers on the actual instantiated map
            var userAgent = "NetAntenna/1.0 (admin@example.com)";
            _baseLayer = Mapsui.Tiling.OpenStreetMap.CreateTileLayer(userAgent);
            _baseLayer.Name = "Base";
            TowerMap.Map.Layers.Add(_baseLayer);

            TowerMap.Map.Layers.Add(_contourLayer);
            TowerMap.Map.Layers.Add(_towerLayer);

            _mapInitialized = true;
        }

        if (DataContext is TowerMapViewModel vm)
        {
            vm.Towers.CollectionChanged -= OnTowersChanged; // Prevent duplicate subscriptions
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            
            vm.Towers.CollectionChanged += OnTowersChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            SwitchBaseLayer(vm.SelectedMapLayer); // Ensure the selected layer is applied on startup
            DrawTowersAndContours(vm.Towers);
            CenterMap(vm.UserLat, vm.UserLng);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (DataContext is not TowerMapViewModel vm) return;

        switch (e.PropertyName)
        {
            case nameof(TowerMapViewModel.UserLat):
            case nameof(TowerMapViewModel.UserLng):
                CenterMap(vm.UserLat, vm.UserLng);
                break;

            case nameof(TowerMapViewModel.SelectedMapLayer):
                SwitchBaseLayer(vm.SelectedMapLayer);
                break;

            case nameof(TowerMapViewModel.ZoomToLocationRequested):
                CenterMap(vm.UserLat, vm.UserLng);
                break;

            case nameof(TowerMapViewModel.ZoomInToMeRequested):
                TowerMap.Map?.Navigator?.ZoomIn();
                break;

            case nameof(TowerMapViewModel.ZoomOutFromMeRequested):
                TowerMap.Map?.Navigator?.ZoomOut();
                break;

            case nameof(TowerMapViewModel.ResetMapRequested):
                var (x, y) = SphericalMercator.FromLonLat(-98.5795, 39.8283);
                TowerMap.Map?.Navigator?.CenterOnAndZoomTo(new MPoint(x, y), 100000);
                break;

            case nameof(TowerMapViewModel.Towers):
                DrawTowersAndContours(vm.Towers);
                break;
        }
    }

    private void SwitchBaseLayer(MapLayer layer)
    {
        if (TowerMap.Map is null) return;

        // Remove the current base layer
        if (_baseLayer is not null)
            TowerMap.Map.Layers.Remove(_baseLayer);

        // OpenStreetMap requires a valid User-Agent, otherwise it returns 403.
        var userAgent = "NetAntenna/1.0 (admin@example.com)";
        
        _baseLayer = layer switch
        {
            MapLayer.Terrain   => new TileLayer(BruTile.Predefined.KnownTileSources.Create(KnownTileSource.EsriWorldTopo, userAgent))  { Name = "Base" },
            MapLayer.Satellite => new TileLayer(BruTile.Predefined.KnownTileSources.Create(KnownTileSource.BingAerial, userAgent)) { Name = "Base" },
            _                  => Mapsui.Tiling.OpenStreetMap.CreateTileLayer(userAgent)
        };
        _baseLayer.Name = "Base";

        // Insert at index 0 so contours and towers remain on top
        TowerMap.Map.Layers.Insert(0, _baseLayer);
        TowerMap.Refresh();
    }

    private void CenterMap(string latStr, string lngStr)
    {
        if (double.TryParse(latStr, out var lat) && double.TryParse(lngStr, out var lng))
        {
            var (x, y) = SphericalMercator.FromLonLat(lng, lat);
            
            // Resolution of ~200 m/pixel over roughly 800-1000 pixels gives a 100-120 mile view.
            TowerMap.Map?.Navigator?.CenterOnAndZoomTo(new MPoint(x, y), 200); 
            
            // Re-draw towers whenever location changes so user pin is updated
            if (DataContext is TowerMapViewModel vm)
            {
                DrawTowersAndContours(vm.Towers);
            }
        }
    }

    private void OnTowersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is TowerMapViewModel vm)
        {
            DrawTowersAndContours(vm.Towers);
        }
    }

    private void DrawTowersAndContours(IEnumerable<FccTower> towers)
    {
        var towerFeatures = new List<GeometryFeature>();
        var contourFeatures = new List<GeometryFeature>();

        // We will store the user's projected coordinates to draw lines to them
        double? projectedUserX = null;
        double? projectedUserY = null;
        double? userLat = null;
        double? userLng = null;

        // Add User Location Pin
        if (DataContext is TowerMapViewModel vm && 
            double.TryParse(vm.UserLat, out var uLat) && 
            double.TryParse(vm.UserLng, out var uLng))
        {
            userLat = uLat;
            userLng = uLng;
            var (ux, uy) = SphericalMercator.FromLonLat(uLng, uLat);
            projectedUserX = ux;
            projectedUserY = uy;

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
            towerFeatures.Add(userFeature);
        }

        foreach (var t in towers)
        {
            // 1. Plot Tower Pin
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

            towerFeatures.Add(feature);

            // Draw line from tower to user if location is set
            if (projectedUserX.HasValue && projectedUserY.HasValue && userLat.HasValue && userLng.HasValue)
            {
                var predictedDbm = _rfEngine.CalculatePredictedPowerDbm(t, userLat.Value, userLng.Value);
                var lineColor = GetSignalColor(predictedDbm);

                var lineFeature = new GeometryFeature
                {
                    Geometry = new LineString(new[]
                    {
                        new Coordinate(x, y),
                        new Coordinate(projectedUserX.Value, projectedUserY.Value)
                    })
                };

                lineFeature.Styles.Add(new VectorStyle
                {
                    Line = new Pen { Color = lineColor, Width = 1.5, PenStyle = PenStyle.Dash }
                });

                contourFeatures.Add(lineFeature);
            }

            // 2. Plot Theoretical Signal Contour (-90 dBm Edge)
            var radiusKm = _rfEngine.CalculateContourDistanceKm(t, -90.0);
            if (radiusKm > 0)
            {
                var polygon = CreateCirclePolygon(t.Latitude, t.Longitude, radiusKm);
                var contourCircleFeature = new GeometryFeature
                {
                    Geometry = polygon
                };

                contourCircleFeature.Styles.Add(new VectorStyle
                {
                    Fill = new Brush(new Color(0, 0, 0, 0)), // Fully transparent
                    Outline = new Pen { Color = new Color(255, 193, 7, 20), Width = 0.5, PenStyle = PenStyle.Dash }
                });

                contourFeatures.Add(contourCircleFeature);
            }
        } // Missing brace for foreach loop

        _towerLayer.Features = towerFeatures;
        _contourLayer.Features = contourFeatures;
        
        TowerMap.Refresh();
    }

    private static Color GetSignalColor(double dbm)
    {
        // Map signal strength from Red (bad) to Green (good)
        // Adjusting bounds to be more restrictive:
        // -35 dBm is incredibly strong (Green)
        // -50 dBm is moderate/yellow (Yellow)
        // -65 dBm or lower is weak/unreliable (Red)
        double minDbm = -65.0;
        double maxDbm = -35.0;
        
        double normalized = (dbm - minDbm) / (maxDbm - minDbm);
        normalized = Math.Max(0.0, Math.Min(1.0, normalized));
        
        int r, g;
        if (normalized < 0.5)
        {
            // Red to Yellow.
            r = 255;
            g = (int)(255 * (normalized * 2));
        }
        else
        {
            // Yellow to Green.
            r = (int)(255 * (1.0 - (normalized - 0.5) * 2));
            g = 255;
        }
        
        // Mapsui color is Color(r, g, b, alpha)
        return new Color(r, g, 0, 200); // Set alpha to 200 for stronger visibility against pale maps
    }

    private static Polygon CreateCirclePolygon(double centerLat, double centerLon, double radiusKm)
    {
        const int numPoints = 64; // resolution of the circle
        var coordinates = new Coordinate[numPoints + 1];
        
        for (int i = 0; i < numPoints; i++)
        {
            var bearingDeg = i * (360.0 / numPoints);
            var (destLat, destLon) = CalculateDestinationLocation(centerLat, centerLon, radiusKm, bearingDeg);
            
            // Convert back to Spherical Mercator for Mapsui
            var (x, y) = SphericalMercator.FromLonLat(destLon, destLat);
            coordinates[i] = new Coordinate(x, y);
        }
        
        // Close the ring
        coordinates[numPoints] = new Coordinate(coordinates[0].X, coordinates[0].Y);
        
        return new Polygon(new LinearRing(coordinates));
    }

    private static (double descLat, double descLon) CalculateDestinationLocation(double startLat, double startLon, double distanceKm, double bearingDeg)
    {
        const double EarthRadiusKm = 6371.0;
        
        var startLatRad = startLat * Math.PI / 180.0;
        var startLonRad = startLon * Math.PI / 180.0;
        var bearingRad = bearingDeg * Math.PI / 180.0;
        var angularDistance = distanceKm / EarthRadiusKm;

        var destLatRad = Math.Asin(Math.Sin(startLatRad) * Math.Cos(angularDistance) +
                                   Math.Cos(startLatRad) * Math.Sin(angularDistance) * Math.Cos(bearingRad));

        var destLonRad = startLonRad + Math.Atan2(Math.Sin(bearingRad) * Math.Sin(angularDistance) * Math.Cos(startLatRad),
                             Math.Cos(angularDistance) - Math.Sin(startLatRad) * Math.Sin(destLatRad));

        return (destLatRad * 180.0 / Math.PI, destLonRad * 180.0 / Math.PI);
    }
}
