using Serilog;
using Serilog.Core;
using Serilog.Events;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Logging;

/// <summary>
/// Serilog bootstrap for the standalone New System host (<c>SiNet.App.Wpf</c>).
/// Keeps Serilog types out of the WPF project (see <c>docs/LOGGING.md</c> §9).
/// <para>
/// Two-phase by design: <see cref="ConfigureDefault"/> boots a local-only logger before the vault
/// gate, then <see cref="ConfigureCentral"/> rebuilds the full pipeline (local + central network
/// sink) once the SQL connection string is known. The per-user toggle moves
/// <see cref="LocalFileLevelSwitch"/> and therefore can never silence the central sink.
/// </para>
/// </summary>
public static class StandaloneHostLoggingBootstrap
{
    private const string HostPropertyName = "Host";
    private const string HostPropertyValue = "SiNet.App.Wpf";

    /// <summary>
    /// Controls the LOCAL file sink only. The central sink keeps its own
    /// <c>Logging.Client.CentralLevel</c> so operations logging survives the user toggle.
    /// </summary>
    private static readonly LoggingLevelSwitch LocalFileLevelSwitch = new(LogEventLevel.Debug);

    private static string? _sqlConnectionString;
    private static string? _localLogDirectory;

    /// <summary>
    /// Phase 1 — local-only logger, verbose. Runs before the vault gate, so no DB read and no
    /// probe of the central UNC share.
    /// </summary>
    public static void ConfigureDefault()
    {
        var defaults = UserAppSettingsDefaults.Create().Logging;
        SetLocalLevel(enabled: true);
        Rebuild(defaults.LogDirectory);
    }

    /// <summary>
    /// Phase 2 — rebuilds the pipeline with the central network sink using DB-driven settings.
    /// Must run before the service provider is built, because <see cref="SerilogAppLogger"/>
    /// captures <see cref="Log.Logger"/> in its constructor.
    /// </summary>
    /// <param name="sqlConnectionString">Vault-resolved SiNet DB connection string.</param>
    public static void ConfigureCentral(string? sqlConnectionString)
    {
        _sqlConnectionString = string.IsNullOrWhiteSpace(sqlConnectionString)
            ? null
            : sqlConnectionString.Trim();

        Rebuild(_localLogDirectory);
        LogSinkDiagnostics();
    }

    /// <summary>
    /// Applies the per-user toggle. Moves the local level switch instead of rebuilding, so every
    /// other sink survives. Only a directory change forces a rebuild.
    /// </summary>
    public static void ApplyUserLogging(UserLoggingSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        SetLocalLevel(settings.LoggingEnabled);

        var directory = ResolveDirectory(settings.LogDirectory);
        if (!string.Equals(directory, _localLogDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Rebuild(settings.LogDirectory);
        }
    }

    public static void Info(string message) => Log.Information(message);

    public static void Warning(string message) => Log.Warning(message);

    public static void Warning(Exception exception, string message) => Log.Warning(exception, message);

    public static void Fatal(string message) => Log.Fatal(message);

    public static void Fatal(Exception exception, string message) => Log.Fatal(exception, message);

    public static void Debug(Exception exception, string messageTemplate, params object?[] propertyValues)
        => Log.Debug(exception, messageTemplate, propertyValues);

    public static void CloseAndFlush() => Log.CloseAndFlush();

    private static void SetLocalLevel(bool enabled) =>
        LocalFileLevelSwitch.MinimumLevel = enabled ? LogEventLevel.Debug : LogEventLevel.Fatal;

    private static void Rebuild(string? logDirectory)
    {
        var directory = ResolveDirectory(logDirectory);
        Directory.CreateDirectory(directory);
        _localLogDirectory = directory;

        var config = BuildConfig(directory);

        // Release the previous file handles before swapping — the directory may have changed.
        Log.CloseAndFlush();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.WithProperty(HostPropertyName, HostPropertyValue)
            .AddSiNetCentralLogging(config)
            .CreateLogger();
    }

    private static CentralLoggingConfig BuildConfig(string directory)
    {
        // Standalone reuses SiNetApp.Client: the Logging.Client.* SystemSettings rows already exist
        // and are managed by the Admin UI (docs/LOGGING.md §9.3).
        if (_sqlConnectionString is null)
        {
            return new CentralLoggingConfig
            {
                App = SiNetApp.Client,
                CentralLogPath = null,
                LocalLogDirectory = directory,
                LocalFileLevelSwitch = LocalFileLevelSwitch,
            };
        }

        return CentralLoggingSettings.LoadFromDatabase(
            _sqlConnectionString,
            SiNetApp.Client,
            enableConsole: false,
            localFileLevelSwitch: LocalFileLevelSwitch) with
        {
            LocalLogDirectory = directory,
        };
    }

    private static void LogSinkDiagnostics()
    {
        Log.Information(
            "[STARTUP] Logging sinks. Local={Local} Central={Central} CentralEnabled={Enabled}",
            CentralLoggingBuilder.LocalSinkTargetFile ?? "(none)",
            CentralLoggingBuilder.CentralSinkTargetFile ?? "(disabled — Logging.CentralLogPath empty)",
            CentralLoggingBuilder.CentralSinkEnabled);

        if (CentralLoggingBuilder.CentralSinkBootstrapError is { } centralError)
        {
            Log.Warning(
                "[STARTUP] Central log sink unavailable: {Error}",
                centralError);
        }
    }

    private static string ResolveDirectory(string? logDirectory) =>
        string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiNet",
                "Logs")
            : logDirectory.Trim();
}
