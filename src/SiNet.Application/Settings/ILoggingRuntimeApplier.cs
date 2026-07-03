namespace SiNet.Application.Settings;

/// <summary>
/// Host boundary: applies per-user logging settings to the live Serilog pipeline without exposing
/// legacy <c>AppLogger</c> to <c>SiNet.App.Wpf</c>. Implemented in the production host (V2).
/// </summary>
public interface ILoggingRuntimeApplier
{
    void ApplyUserLogging(UserLoggingSettingsDto settings);
}
