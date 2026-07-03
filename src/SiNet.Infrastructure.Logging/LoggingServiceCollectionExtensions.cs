using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Logging;

/// <summary>
/// Modular DI registration for the logging module (see <c>docs/LOGGING.md</c>, <c>docs/SETTINGS.md</c>).
/// </summary>
public static class LoggingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ConsoleAppLogger"/> — scaffold/tests only. Production New System host
    /// should call <see cref="AddSiNetSerilogLogging"/> after Serilog bootstrap instead.
    /// </summary>
    public static IServiceCollection AddSiNetLogging(this IServiceCollection services)
    {
        services.AddSingleton<IAppLogger, ConsoleAppLogger>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="SerilogAppLogger"/> over the host-configured <c>Log.Logger</c> pipeline.
    /// Call only after the host static/bootstrap Serilog configuration has run.
    /// </summary>
    public static IServiceCollection AddSiNetSerilogLogging(this IServiceCollection services)
    {
        services.AddSingleton<IAppLogger, SerilogAppLogger>();
        return services;
    }

    /// <summary>
    /// Registers per-user settings (<see cref="IAppSettingsService"/> →
    /// <see cref="JsonAppSettingsService"/>). Stage 5 — see <c>docs/SETTINGS.md</c>.
    /// </summary>
    public static IServiceCollection AddSiNetUserLoggingSettings(this IServiceCollection services)
    {
        services.AddSingleton<IAppSettingsService, JsonAppSettingsService>();
        return services;
    }
}
