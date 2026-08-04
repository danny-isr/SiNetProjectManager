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

    /// <summary>Toolbar / input row MinHeight scaled from Normal font (default 26 @ 12).</summary>
    public const string ControlRowHeight = "SiControlRowHeight";

    /// <summary>Compact toolbar row MinHeight scaled from Small font (default 24 @ 10.8).</summary>
    public const string CompactControlRowHeight = "SiCompactControlRowHeight";

    /// <summary>Popup / dropdown list MaxHeight scaled from Normal font (default 280 @ 12).</summary>
    public const string PopupListMaxHeight = "SiPopupListMaxHeight";

    public const string PrimaryBrush = "SiPrimaryBrush";
    public const string SecondaryBrush = "SiSecondaryBrush";
    public const string BackgroundBrush = "SiBackgroundBrush";
    public const string ForegroundBrush = "SiForegroundBrush";

    public const string BorderBrush = "SiBorderBrush";
    public const string MutedForegroundBrush = "SiMutedForegroundBrush";
    public const string SurfaceBrush = "SiSurfaceBrush";
    public const string OnPrimaryBrush = "SiOnPrimaryBrush";
    public const string DangerBrush = "SiDangerBrush";
    public const string DangerSurfaceBrush = "SiDangerSurfaceBrush";
    public const string WarningBrush = "SiWarningBrush";
    public const string SuccessBrush = "SiSuccessBrush";

    public const string TreePhysicalBrush = "SiTreePhysicalBrush";
    public const string TreeMissingBrush = "SiTreeMissingBrush";
    public const string TreeTypeBrush = "SiTreeTypeBrush";
    public const string TreeEmptyBrush = "SiTreeEmptyBrush";

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

    /// <summary>Chrome heights derived from font sizes (see docs/SETTINGS.md §9 chrome height policy).</summary>
    public static IReadOnlyList<string> AllChromeSizeKeys { get; } =
    [
        ControlRowHeight,
        CompactControlRowHeight,
        PopupListMaxHeight,
    ];

    /// <summary>Brushes driven by per-user appearance JSON.</summary>
    public static IReadOnlyList<string> AppearanceBrushKeys { get; } =
    [
        PrimaryBrush,
        SecondaryBrush,
        BackgroundBrush,
        ForegroundBrush,
    ];

    /// <summary>Product-fixed structural / state brushes (not overwritten by the color picker).</summary>
    public static IReadOnlyList<string> SemanticBrushKeys { get; } =
    [
        BorderBrush,
        MutedForegroundBrush,
        SurfaceBrush,
        OnPrimaryBrush,
        DangerBrush,
        DangerSurfaceBrush,
        WarningBrush,
        SuccessBrush,
        TreePhysicalBrush,
        TreeMissingBrush,
        TreeTypeBrush,
        TreeEmptyBrush,
    ];

    /// <summary>All theme brush keys defined in BrushResources.xaml (appearance + structural/semantic).</summary>
    public static IReadOnlyList<string> AllBrushKeys { get; } =
    [
        PrimaryBrush,
        SecondaryBrush,
        BackgroundBrush,
        ForegroundBrush,
        BorderBrush,
        MutedForegroundBrush,
        SurfaceBrush,
        OnPrimaryBrush,
        DangerBrush,
        DangerSurfaceBrush,
        WarningBrush,
        SuccessBrush,
        TreePhysicalBrush,
        TreeMissingBrush,
        TreeTypeBrush,
        TreeEmptyBrush,
    ];
}
