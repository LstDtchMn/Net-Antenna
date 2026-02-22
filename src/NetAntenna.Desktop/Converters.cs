using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NetAntenna.Desktop;

/// <summary>
/// Value converters for AXAML data binding.
/// </summary>
public static class Converters
{
    /// <summary>Converts bool (IsConnected) to green/red ellipse color.</summary>
    public static readonly IValueConverter BoolToConnectionColor =
        new FuncValueConverter<bool, IBrush>(connected =>
            connected ? new SolidColorBrush(Color.Parse("#4CAF50")) : new SolidColorBrush(Color.Parse("#F44336")));

    /// <summary>Highlights the active nav item.</summary>
    public static readonly IValueConverter NavItemBackground =
        new NavItemBackgroundConverter();

    /// <summary>Converts signal value (0-100) to color brush.</summary>
    public static readonly IValueConverter SignalToColor =
        new FuncValueConverter<int, IBrush>(value => value switch
        {
            >= 80 => new SolidColorBrush(Color.Parse("#4CAF50")),
            >= 50 => new SolidColorBrush(Color.Parse("#FFC107")),
            > 0 => new SolidColorBrush(Color.Parse("#F44336")),
            _ => new SolidColorBrush(Color.Parse("#757575"))
        });

    /// <summary>Converts string hex color to brush.</summary>
    public static readonly IValueConverter StringToBrush =
        new FuncValueConverter<string, IBrush>(hex =>
            new SolidColorBrush(Color.Parse(hex ?? "#757575")));

    /// <summary>Bool to star text for favorites.</summary>
    public static readonly IValueConverter BoolToStar =
        new FuncValueConverter<bool, string>(fav => fav ? "⭐" : "☆");

    /// <summary>Bool to eye text for hidden.</summary>
    public static readonly IValueConverter BoolToEye =
        new FuncValueConverter<bool, string>(hidden => hidden ? "👁‍🗨" : "👁");

    /// <summary>Bool to logging button text.</summary>
    public static readonly IValueConverter BoolToLoggingText =
        new FuncValueConverter<bool, string>(logging => logging ? "⏹ Stop Logging" : "▶ Start Logging");

    /// <summary>Converts bool (HasLock) to a border color for Spectrum Overview.</summary>
    public static readonly IValueConverter LockToColor =
        new FuncValueConverter<bool, IBrush>(hasLock =>
            hasLock ? new SolidColorBrush(Color.Parse("#A6E3A1")) : new SolidColorBrush(Color.Parse("#313244")));
}

public class NavItemBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = value as string;
        var target = parameter as string;
        return selected == target
            ? new SolidColorBrush(Color.Parse("#313244"))
            : new SolidColorBrush(Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
