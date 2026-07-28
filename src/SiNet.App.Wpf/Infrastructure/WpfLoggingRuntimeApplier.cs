using SiNet.Application.Settings;
using SiNet.Infrastructure.Logging;

namespace SiNet.App.Wpf.Infrastructure;

/// <summary>
/// Applies per-user logging settings via <see cref="StandaloneHostLoggingBootstrap"/>
/// (native replacement for V2 <c>LegacyLoggingRuntimeApplier</c> / <c>AppLogger</c>).
/// </summary>
public sealed class WpfLoggingRuntimeApplier : ILoggingRuntimeApplier
{
    public void ApplyUserLogging(UserLoggingSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        StandaloneHostLoggingBootstrap.ApplyUserLogging(settings);
    }
}
