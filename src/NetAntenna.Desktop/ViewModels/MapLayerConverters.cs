using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NetAntenna.Desktop.ViewModels;

/// <summary>
/// Static converters used by TowerMapView to highlight the active map layer button.
/// Usage in XAML: Converter={x:Static vm:MapLayerConverters.StreetBackground}
/// The converter receives the current SelectedMapLayer value and returns active/inactive brush.
/// </summary>
public static class MapLayerConverters
{
    public static readonly IValueConverter StreetBackground   = new LayerBrushConverter(MapLayer.Street,   activeBg: "#00BCD4", inactiveBg: "Transparent");
    public static readonly IValueConverter TerrainBackground  = new LayerBrushConverter(MapLayer.Terrain,  activeBg: "#00BCD4", inactiveBg: "Transparent");
    public static readonly IValueConverter SatelliteBackground= new LayerBrushConverter(MapLayer.Satellite,activeBg: "#00BCD4", inactiveBg: "Transparent");

    public static readonly IValueConverter StreetForeground   = new LayerBrushConverter(MapLayer.Street,   activeBg: "White", inactiveBg: "#A6ADC8");
    public static readonly IValueConverter TerrainForeground  = new LayerBrushConverter(MapLayer.Terrain,  activeBg: "White", inactiveBg: "#A6ADC8");
    public static readonly IValueConverter SatelliteForeground= new LayerBrushConverter(MapLayer.Satellite,activeBg: "White", inactiveBg: "#A6ADC8");
}

internal sealed class LayerBrushConverter(MapLayer targetLayer, string activeBg, string inactiveBg) : IValueConverter
{
    private static IBrush Parse(string color) =>
        SolidColorBrush.Parse(color);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MapLayer current && current == targetLayer
            ? Parse(activeBg)
            : Parse(inactiveBg);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
