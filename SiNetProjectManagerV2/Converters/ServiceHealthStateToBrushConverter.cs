using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SiNetSQL.Services.Health;

namespace SiNetProjectManagerV2.Converters;

/// <summary>
/// Maps a <see cref="ServiceHealthState"/> (or aggregate state passed as such) to a brush
/// suitable for both the persistent indicator dot and per-row dots inside the popup.
/// </summary>
public sealed class ServiceHealthStateToBrushConverter : IValueConverter
{
    public static readonly Brush Online = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xA8, 0x4A))); // green
    public static readonly Brush Warning = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x95, 0x1A))); // orange
    public static readonly Brush Offline = Freeze(new SolidColorBrush(Color.FromRgb(0xD7, 0x32, 0x2D))); // red
    public static readonly Brush AuthNeeded = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xB1, 0x1A))); // yellow-orange
    public static readonly Brush NotConfigured = Freeze(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E))); // gray
    public static readonly Brush Checking = Freeze(new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4))); // blue
    public static readonly Brush Unknown = Freeze(new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD))); // light gray

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ServiceHealthState s)
            return BrushFor(s);
        return Unknown;
    }

    public static Brush BrushFor(ServiceHealthState state) => state switch
    {
        ServiceHealthState.Online => Online,
        ServiceHealthState.Warning => Warning,
        ServiceHealthState.Offline => Offline,
        ServiceHealthState.RequiresAuthorization => AuthNeeded,
        ServiceHealthState.NotConfigured => NotConfigured,
        ServiceHealthState.Checking => Checking,
        _ => Unknown,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
