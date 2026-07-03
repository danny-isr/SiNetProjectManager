using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Theme;

/// <summary>Applies computed theme values to <see cref="Application.Current"/> dynamic resources.</summary>
public sealed class WpfThemeRuntimeApplier : IThemeRuntimeApplier
{
    public void ApplyUserAppearance(UserAppearanceSettingsDto appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        ThemeResourceLoader.EnsureApplicationResourcesMerged();

        if (System.Windows.Application.Current is not { Resources: { } resources })
        {
            return;
        }

        var computed = ThemeCalculator.Compute(appearance);

        resources[ThemeResourceKeys.FontFamily] = new FontFamily(computed.FontFamily);
        resources[ThemeResourceKeys.TextTinyFontSize] = computed.TextTinyFontSize;
        resources[ThemeResourceKeys.TextSmallFontSize] = computed.TextSmallFontSize;
        resources[ThemeResourceKeys.TextNormalFontSize] = computed.TextNormalFontSize;
        resources[ThemeResourceKeys.TextMediumFontSize] = computed.TextMediumFontSize;
        resources[ThemeResourceKeys.TextLargeFontSize] = computed.TextLargeFontSize;
        resources[ThemeResourceKeys.TextHugeFontSize] = computed.TextHugeFontSize;

        resources[ThemeResourceKeys.ForegroundBrush] = CreateBrush(computed.ForegroundColor);
        resources[ThemeResourceKeys.BackgroundBrush] = CreateBrush(computed.BackgroundColor);
        resources[ThemeResourceKeys.PrimaryBrush] = CreateBrush(computed.PrimaryColor);
        resources[ThemeResourceKeys.SecondaryBrush] = CreateBrush(computed.SecondaryColor);
    }

    internal static SolidColorBrush CreateBrush(string hexColor)
    {
        var normalized = NormalizeHex(hexColor);
        var color = (Color)ColorConverter.ConvertFromString(normalized)!;
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    internal static string NormalizeHex(string hexColor)
    {
        var hex = hexColor.Trim();
        return hex.StartsWith('#') ? hex : "#" + hex;
    }
}

/// <summary>Loads user appearance from settings and applies theme on New System startup.</summary>
public sealed class ThemeStartupInitializer
{
    private readonly IAppSettingsService _appSettings;
    private readonly IThemeRuntimeApplier _themeApplier;

    public ThemeStartupInitializer(IAppSettingsService appSettings, IThemeRuntimeApplier themeApplier)
    {
        _appSettings = appSettings;
        _themeApplier = themeApplier;
    }

    public async Task ApplySavedThemeAsync(CancellationToken cancellationToken = default)
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        var settings = await _appSettings.GetUserAppSettingsAsync(cancellationToken).ConfigureAwait(false);
        _themeApplier.ApplyUserAppearance(settings.Appearance);
    }
}
