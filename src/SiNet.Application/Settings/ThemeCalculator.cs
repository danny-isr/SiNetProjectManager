namespace SiNet.Application.Settings;

/// <summary>Computes resolved font sizes from base size and scale multipliers.</summary>
public static class ThemeCalculator
{
    public static ThemeComputedValues Compute(UserAppearanceSettingsDto appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        return new ThemeComputedValues(
            appearance.FontFamily,
            ComputeFontSize(appearance.BaseFontSize, appearance.TextTinyScale),
            ComputeFontSize(appearance.BaseFontSize, appearance.TextSmallScale),
            ComputeFontSize(appearance.BaseFontSize, appearance.TextNormalScale),
            ComputeFontSize(appearance.BaseFontSize, appearance.TextMediumScale),
            ComputeFontSize(appearance.BaseFontSize, appearance.TextLargeScale),
            ComputeFontSize(appearance.BaseFontSize, appearance.TextHugeScale),
            appearance.ForegroundColor,
            appearance.BackgroundColor,
            appearance.PrimaryColor,
            appearance.SecondaryColor);
    }

    public static double ComputeFontSize(double baseFontSize, double scale)
        => Math.Round(baseFontSize * scale, 1);
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
    string ForegroundColor,
    string BackgroundColor,
    string PrimaryColor,
    string SecondaryColor);
