using System.Windows;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Theme;

/// <summary>
/// Applies themed window chrome (dynamic background / foreground / typography inheritance root).
/// OS-drawn title-bar text is not controlled by WPF FontSize — only in-window content inherits.
/// </summary>
public static class ThemeWindowChrome
{
    public static void ApplyThemedWindowBackground(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        window.SetResourceReference(Window.BackgroundProperty, ThemeResourceKeys.BackgroundBrush);
        window.SetResourceReference(Window.ForegroundProperty, ThemeResourceKeys.ForegroundBrush);
        window.SetResourceReference(Window.FontFamilyProperty, ThemeResourceKeys.FontFamily);
        window.SetResourceReference(Window.FontSizeProperty, ThemeResourceKeys.TextNormalFontSize);
    }
}
