namespace SiNet.Application.Settings;

/// <summary>Computes resolved font sizes and chrome heights from base size and scale multipliers.</summary>
public static class ThemeCalculator
{
    /// <summary>At default Normal=12 → control row 26 (ProjectSelector / toolbar inputs).</summary>
    public const double ControlRowHeightPerNormal = 26.0 / 12.0;

    /// <summary>At default Small=10.8 → compact row 24.</summary>
    public const double CompactControlRowHeightPerSmall = 24.0 / 10.8;

    /// <summary>At default Normal=12 → popup list MaxHeight 280 (ProjectSelector results).</summary>
    public const double PopupListMaxHeightPerNormal = 280.0 / 12.0;

    public static ThemeComputedValues Compute(UserAppearanceSettingsDto appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        var tiny = ComputeFontSize(appearance.BaseFontSize, appearance.TextTinyScale);
        var small = ComputeFontSize(appearance.BaseFontSize, appearance.TextSmallScale);
        var normal = ComputeFontSize(appearance.BaseFontSize, appearance.TextNormalScale);
        var medium = ComputeFontSize(appearance.BaseFontSize, appearance.TextMediumScale);
        var large = ComputeFontSize(appearance.BaseFontSize, appearance.TextLargeScale);
        var huge = ComputeFontSize(appearance.BaseFontSize, appearance.TextHugeScale);

        return new ThemeComputedValues(
            appearance.FontFamily,
            tiny,
            small,
            normal,
            medium,
            large,
            huge,
            ComputeChromeSize(normal, ControlRowHeightPerNormal),
            ComputeChromeSize(small, CompactControlRowHeightPerSmall),
            ComputeChromeSize(normal, PopupListMaxHeightPerNormal),
            appearance.ForegroundColor,
            appearance.BackgroundColor,
            appearance.PrimaryColor,
            appearance.SecondaryColor);
    }

    public static double ComputeFontSize(double baseFontSize, double scale)
        => Math.Round(baseFontSize * scale, 1);

    public static double ComputeChromeSize(double fontSize, double factor)
        => Math.Round(fontSize * factor, 0);
}

/// <summary>Resolved theme values applied to WPF dynamic resources.</summary>
public sealed record ThemeComputedValues(
    string FontFamily,
    double TextTinyFontSize,
    double TextSmallFontSize,
    double TextNormalFontSize,
    double TextMediumFontSize,
    double TextLargeFontSize,
    double TextHugeFontSize,
    double ControlRowHeight,
    double CompactControlRowHeight,
    double PopupListMaxHeight,
    string ForegroundColor,
    string BackgroundColor,
    string PrimaryColor,
    string SecondaryColor);
