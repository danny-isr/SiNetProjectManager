using Serilog;
using Serilog.Events;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Logging;

/// <summary>
/// Serilog bootstrap for the standalone New System host (<c>SiNet.App.Wpf</c>).
/// Keeps Serilog types out of the WPF project (see <c>docs/LOGGING.md</c>).
/// </summary>
public static class StandaloneHostLoggingBootstrap
{
    public static void ConfigureDefault()
    {
        var defaults = UserAppSettingsDefaults.Create().Logging;
        ApplyUserLogging(defaults with { LoggingEnabled = true });
    }

    public static void ApplyUserLogging(UserLoggingSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = string.IsNullOrWhiteSpace(settings.LogDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiNet",
                "Logs")
            : settings.LogDirectory.Trim();

        Directory.CreateDirectory(directory);

        var minLevel = settings.LoggingEnabled ? LogEventLevel.Debug : LogEventLevel.Fatal;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.File(
                path: Path.Combine(directory, "SiNet-Standalone-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }

    public static void Info(string message) => Log.Information(message);

    public static void Warning(string message) => Log.Warning(message);

    public static void Warning(Exception exception, string message) => Log.Warning(exception, message);

    public static void Fatal(string message) => Log.Fatal(message);

    public static void Fatal(Exception exception, string message) => Log.Fatal(exception, message);

    public static void Debug(Exception exception, string messageTemplate, params object?[] propertyValues)
        => Log.Debug(exception, messageTemplate, propertyValues);

    public static void CloseAndFlush() => Log.CloseAndFlush();
}
