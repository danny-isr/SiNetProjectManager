namespace SiNet.Application.Settings;

/// <summary>
/// Host/WPF boundary: applies per-user appearance/theme to live Application resources without
/// exposing legacy <c>SettingsManager</c> to <c>SiNet.App.Wpf</c> view models.
/// </summary>
public interface IThemeRuntimeApplier
{
    void ApplyUserAppearance(UserAppearanceSettingsDto appearance);
}
