namespace SiNet.Application.Settings;

/// <summary>Appearance / theme fields from per-user <c>settings.json</c>.</summary>
public sealed record UserAppearanceSettingsDto(
    string FontFamily,
    double FontSize,
    string ForegroundColor,
    string BackgroundColor);

/// <summary>General per-user behavior flags.</summary>
public sealed record UserBehaviorSettingsDto(bool AllowMultipleInstances);

/// <summary>Floating window opacity (runtime for legacy floating windows).</summary>
public sealed record UserFloatingWindowOpacityDto(
    double ActiveOpacity,
    double IdleOpacity);

/// <summary>Saved geometry for a floating window (applied on next open).</summary>
public sealed record FloatingWindowGeometryDto(
    double Top,
    double Left,
    double Width,
    double Height);

/// <summary>All per-user fields persisted in <c>settings.json</c>.</summary>
public sealed record UserAppSettingsDto(
    UserAppearanceSettingsDto Appearance,
    UserBehaviorSettingsDto Behavior,
    UserLoggingSettingsDto Logging,
    UserFloatingWindowOpacityDto FloatingOpacity,
    FloatingWindowGeometryDto FloatingTasks,
    FloatingWindowGeometryDto FloatingInspection,
    bool EnableAuthorizationTestMode);

/// <summary>Legacy defaults from <c>AppSettings</c> constructor.</summary>
public static class UserAppSettingsDefaults
{
    public const string FontFamily = "Segoe UI";
    public const double FontSize = 12.0;
    public const string ForegroundColor = "#000000";
    public const string BackgroundColor = "#FFFFFF";
    public const bool AllowMultipleInstances = true;
    public const bool LoggingEnabled = false;
    public const double FloatingActiveOpacity = 1.0;
    public const double FloatingIdleOpacity = 0.7;
    public const double FloatingTasksWidth = 420;
    public const double FloatingTasksHeight = 560;
    public const double FloatingInspectionWidth = 420;
    public const double FloatingInspectionHeight = 850;

    public static UserAppSettingsDto Create() => new(
        new UserAppearanceSettingsDto(FontFamily, FontSize, ForegroundColor, BackgroundColor),
        new UserBehaviorSettingsDto(AllowMultipleInstances),
        new UserLoggingSettingsDto(
            LoggingEnabled,
            null,
            string.Empty,
            string.Empty),
        new UserFloatingWindowOpacityDto(FloatingActiveOpacity, FloatingIdleOpacity),
        new FloatingWindowGeometryDto(double.NaN, double.NaN, FloatingTasksWidth, FloatingTasksHeight),
        new FloatingWindowGeometryDto(double.NaN, double.NaN, FloatingInspectionWidth, FloatingInspectionHeight),
        EnableAuthorizationTestMode: false);
}
