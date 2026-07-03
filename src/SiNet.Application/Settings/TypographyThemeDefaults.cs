namespace SiNet.Application.Settings;

/// <summary>Default typography scale multipliers and validation ranges (Stage 6).</summary>
public static class TypographyThemeDefaults
{
    public const double TextTinyScale = 0.80;
    public const double TextSmallScale = 0.90;
    public const double TextNormalScale = 1.00;
    public const double TextMediumScale = 1.20;
    public const double TextLargeScale = 1.50;
    public const double TextHugeScale = 1.80;

    public const string PrimaryColor = "#1F3A5F";
    public const string SecondaryColor = "#757575";

    public static UserAppearanceSettingsDto CreateDefaultAppearance()
        => new(
            UserAppSettingsDefaults.FontFamily,
            UserAppSettingsDefaults.BaseFontSize,
            TextTinyScale,
            TextSmallScale,
            TextNormalScale,
            TextMediumScale,
            TextLargeScale,
            TextHugeScale,
            UserAppSettingsDefaults.ForegroundColor,
            UserAppSettingsDefaults.BackgroundColor,
            PrimaryColor,
            SecondaryColor);

    public static bool TryValidateScales(
        double textTinyScale,
        double textSmallScale,
        double textNormalScale,
        double textMediumScale,
        double textLargeScale,
        double textHugeScale,
        out string error)
    {
        if (!InRange(textTinyScale, 0.60, 0.95))
        {
            error = "מכפלת Tiny חייבת להיות בין 0.60 ל-0.95.";
            return false;
        }

        if (!InRange(textSmallScale, 0.70, 1.00))
        {
            error = "מכפלת Small חייבת להיות בין 0.70 ל-1.00.";
            return false;
        }

        if (!InRange(textNormalScale, 0.90, 1.10))
        {
            error = "מכפלת Normal חייבת להיות בין 0.90 ל-1.10.";
            return false;
        }

        if (!InRange(textMediumScale, 1.05, 1.35))
        {
            error = "מכפלת Medium חייבת להיות בין 1.05 ל-1.35.";
            return false;
        }

        if (!InRange(textLargeScale, 1.30, 1.80))
        {
            error = "מכפלת Large חייבת להיות בין 1.30 ל-1.80.";
            return false;
        }

        if (!InRange(textHugeScale, 1.60, 2.40))
        {
            error = "מכפלת Huge חייבת להיות בין 1.60 ל-2.40.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsValidHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = value.Trim();
        if (!hex.StartsWith('#'))
        {
            hex = "#" + hex;
        }

        return hex.Length is 7 or 9 && hex.Skip(1).All(static c => Uri.IsHexDigit(c));
    }

    private static bool InRange(double value, double min, double max)
        => value >= min && value <= max;
}
