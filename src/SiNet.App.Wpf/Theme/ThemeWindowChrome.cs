using System.Windows;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Theme;

/// <summary>Applies themed window chrome (dynamic background / foreground).</summary>
public static class ThemeWindowChrome
{
    public static void ApplyThemedWindowBackground(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        window.SetResourceReference(Window.BackgroundProperty, ThemeResourceKeys.BackgroundBrush);
        window.SetResourceReference(Window.ForegroundProperty, ThemeResourceKeys.ForegroundBrush);
    }
}
