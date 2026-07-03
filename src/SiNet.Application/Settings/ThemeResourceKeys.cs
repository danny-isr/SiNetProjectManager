namespace SiNet.Application.Settings;

/// <summary>Dynamic resource keys for the native New System theme (see docs/SETTINGS.md §9).</summary>
public static class ThemeResourceKeys
{
    public const string FontFamily = "SiFontFamily";
    public const string TextTinyFontSize = "SiTextTinyFontSize";
    public const string TextSmallFontSize = "SiTextSmallFontSize";
    public const string TextNormalFontSize = "SiTextNormalFontSize";
    public const string TextMediumFontSize = "SiTextMediumFontSize";
    public const string TextLargeFontSize = "SiTextLargeFontSize";
    public const string TextHugeFontSize = "SiTextHugeFontSize";

    public const string PrimaryBrush = "SiPrimaryBrush";
    public const string SecondaryBrush = "SiSecondaryBrush";
    public const string BackgroundBrush = "SiBackgroundBrush";
    public const string ForegroundBrush = "SiForegroundBrush";

    public const string TextTinyStyle = "SiTextTinyStyle";
    public const string TextSmallStyle = "SiTextSmallStyle";
    public const string TextNormalStyle = "SiTextNormalStyle";
    public const string TextMediumStyle = "SiTextMediumStyle";
    public const string TextLargeStyle = "SiTextLargeStyle";
    public const string TextHugeStyle = "SiTextHugeStyle";

    public const string PrimaryButtonStyle = "SiPrimaryButtonStyle";
    public const string SecondaryButtonStyle = "SiSecondaryButtonStyle";
    public const string TextBoxStyle = "SiTextBoxStyle";
    public const string ComboBoxStyle = "SiComboBoxStyle";
    public const string SectionHeaderStyle = "SiSectionHeaderStyle";

    public static IReadOnlyList<string> AllFontSizeKeys { get; } =
    [
        TextTinyFontSize,
        TextSmallFontSize,
        TextNormalFontSize,
        TextMediumFontSize,
        TextLargeFontSize,
        TextHugeFontSize,
    ];

    public static IReadOnlyList<string> AllBrushKeys { get; } =
    [
        PrimaryBrush,
        SecondaryBrush,
        BackgroundBrush,
        ForegroundBrush,
    ];
}
