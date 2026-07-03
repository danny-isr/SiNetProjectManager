using SiNet.Application.Settings;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services.Composition;

/// <summary>
/// Applies per-user logging settings to the legacy live pipeline (<see cref="AppLogger"/>) for the
/// shared production host. Not referenced from <c>SiNet.App.Wpf</c>.
/// </summary>
public sealed class LegacyLoggingRuntimeApplier : ILoggingRuntimeApplier
{
    public void ApplyUserLogging(UserLoggingSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppLogger.Configure(
            settings.LoggingEnabled,
            string.IsNullOrWhiteSpace(settings.LogDirectory) ? null : settings.LogDirectory);
    }
}
