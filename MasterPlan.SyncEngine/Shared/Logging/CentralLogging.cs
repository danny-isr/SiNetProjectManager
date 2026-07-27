// ─────────────────────────────────────────────────────────────────────────────
//  CentralLogging — shared logging configuration for every SiNet app.
//
//  Single source of truth: reads logging settings from the SystemSettings
//  table (managed via the Admin UI). All three apps (WPF client,
//  SiOffice.AccService, MasterPlan.SyncEngine) call AddSiNetCentralLogging
//  to get an identical sink layout, output template and enrichers.
//
//  Layout under the central share (default \\si-win-2k19\AutoCAD Data\log):
//      <central>\<AppName>\<Machine>\<User>\<AppName>-yyyyMMdd.log
//  Local logs:
//      <localDir>\<AppName>-yyyyMMdd.log
//
//  No Windows-only APIs are used here so this file can be shared via
//  <Compile Include="..."/> by net10.0 console projects too.
// ─────────────────────────────────────────────────────────────────────────────

using System.Data;
using Microsoft.Data.SqlClient;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace SiNetSQL.Services.Logging;

/// <summary>
/// Logical name of an application — drives the sub-folder under the central
/// log share and the file-name prefix.
/// </summary>
public enum SiNetApp
{
    /// <summary>WPF desktop client (SiNetProjectManagerV2).</summary>
    Client,
    /// <summary>Privileged ACC service running on the office Windows Server (SiOffice.AccService).</summary>
    AccService,
    /// <summary>MasterPlan database sync console (MasterPlan.SyncEngine).</summary>
    SyncEngine
}

/// <summary>
/// Resolved logging configuration consumed by <see cref="CentralLoggingBuilder"/>.
/// Created either via <see cref="CentralLoggingSettings.LoadFromDatabase"/> or directly
/// by tests. Values come from the DB (SystemSettings) with fallback to <see cref="CentralLoggingDefaults"/>.
/// </summary>
public sealed record CentralLoggingConfig
{
    /// <summary>Logical app identifier (controls folder structure and file prefix).</summary>
    public required SiNetApp App { get; init; }

    /// <summary>UNC or local path for the central share. Null/empty = central sink disabled.</summary>
    public string? CentralLogPath { get; init; }

    /// <summary>Local log directory. Null/empty = use <see cref="CentralLoggingDefaults.GetDefaultLocalDirectory"/>.</summary>
    public string? LocalLogDirectory { get; init; }

    /// <summary>Minimum level for the LOCAL rolling-file sink.</summary>
    public LogEventLevel LocalFileMinLevel { get; init; } = LogEventLevel.Information;

    /// <summary>Minimum level for the CENTRAL rolling-file sink.</summary>
    public LogEventLevel CentralMinLevel { get; init; } = LogEventLevel.Warning;

    /// <summary>Local file retention (days).</summary>
    public int LocalRetentionDays { get; init; } = 14;

    /// <summary>Central file retention (days).</summary>
    public int CentralRetentionDays { get; init; } = 90;

    /// <summary>When true, also writes to console (useful for the SyncEngine and dev runs).</summary>
    public bool EnableConsole { get; init; }

    /// <summary>
    /// Optional dynamic level switch for the LOCAL file sink. When supplied,
    /// the sink's MinimumLevel is controlled by this switch instead of
    /// <see cref="LocalFileMinLevel"/> — letting callers (e.g. the WPF Settings UI)
    /// toggle verbose logging at runtime without rebuilding the logger.
    /// </summary>
    public LoggingLevelSwitch? LocalFileLevelSwitch { get; init; }
}

/// <summary>
/// Compile-time defaults for centralized logging — used when the SystemSettings
/// row is missing.
/// </summary>
public static class CentralLoggingDefaults
{
    /// <summary>
    /// Default UNC path for the central log share. Overridable via the
    /// <c>Logging.CentralLogPath</c> SystemSettings row.
    /// </summary>
    public const string DefaultCentralLogPath = @"\\si-win-2k19\AutoCAD Data\log";

    /// <summary>
    /// Output template shared by every sink in every app — keeps the central
    /// log easy to grep across sources.
    /// </summary>
    public const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{App}] [{Machine}] [{User}] " +
        "[P{ProcessId:D5}/T{ThreadId:D3}] [{Level:u4}] {Message:lj}{NewLine}{Exception}";

    /// <summary>Default per-file size limit before rolling.</summary>
    public const long FileSizeLimitBytes = 10_000_000;

    /// <summary>Returns the default local log directory for a given app.</summary>
    public static string GetDefaultLocalDirectory(SiNetApp app) => app switch
    {
        SiNetApp.Client => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiNetProjectManager", "Logs"),
        SiNetApp.AccService => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SiOffice", "AccService", "logs"),
        SiNetApp.SyncEngine => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SiOffice", "MasterPlanSync", "logs"),
        _ => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SiOffice", app.ToString(), "logs")
    };
}

/// <summary>
/// LoggerConfiguration extension that wires the standard SiNet sinks layout.
/// </summary>
public static class CentralLoggingBuilder
{
    /// <summary>
    /// Adds the standard SiNet sinks (local file + central network file +
    /// optional console) and shared enrichers to <paramref name="cfg"/>.
    /// Safe to combine with additional <c>WriteTo.*</c> calls before/after.
    /// </summary>
    /// <remarks>
    /// Failures to create directories (e.g. unreachable UNC) are swallowed —
    /// the logger always boots, even if only the local sink ends up active.
    /// </remarks>
    public static LoggerConfiguration AddSiNetCentralLogging(
        this LoggerConfiguration cfg,
        CentralLoggingConfig config)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentNullException.ThrowIfNull(config);

        var appName = config.App.ToString();
        var machine = SafeMachineName();
        var user = SafeUserName();

        // Shared enrichers — every sink benefits from the same context.
        cfg = cfg
            .Enrich.WithProperty("App", appName)
            .Enrich.WithProperty("Machine", machine)
            .Enrich.WithProperty("User", user)
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .Enrich.With(new ThreadIdEnricher());

        // ─── Local file sink ────────────────────────────────────────────────
        var localDir = string.IsNullOrWhiteSpace(config.LocalLogDirectory)
            ? CentralLoggingDefaults.GetDefaultLocalDirectory(config.App)
            : config.LocalLogDirectory!;

        TryCreateDirectory(localDir);
        var localFile = Path.Combine(localDir, $"{appName}-.log");
        _localSinkTargetDirectory = localDir;
        _localSinkTargetFile = localFile;

        cfg = cfg.WriteTo.Logger(sub =>
        {
            LoggerSinkConfiguration sinks = ApplyLocalLevel(sub, config).WriteTo;
            sinks.Async(a => a.File(
                path: localFile,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: CentralLoggingDefaults.FileSizeLimitBytes,
                retainedFileCountLimit: Math.Max(1, config.LocalRetentionDays),
                outputTemplate: CentralLoggingDefaults.OutputTemplate,
                shared: true));
        });

        // ─── Central network file sink ──────────────────────────────────────
        // <central>\<App>\<Machine>\<User>\<App>-yyyyMMdd.log
        if (!string.IsNullOrWhiteSpace(config.CentralLogPath))
        {
            var centralDir = Path.Combine(config.CentralLogPath!, appName, machine, user);
            var centralFile = Path.Combine(centralDir, $"{appName}-.log");

            // Always record the target — even on failure — so callers can log
            // "we tried to write the central log to X" in the local file.
            _centralSinkTargetDirectory = centralDir;
            _centralSinkTargetFile = centralFile;

            var (ok, probeError) = TryProbeCentralDirectory(centralDir);
            if (ok)
            {
                // Capture Serilog SelfLog into our diagnostic field so any
                // *runtime* sink failure (file locked, share dropped mid-run,
                // retention cleanup error) is preserved instead of being
                // silently dropped. Callers re-emit it via Log.Warning.
                EnableSelfLogCapture(config);

                cfg = cfg.WriteTo.Logger(sub => sub
                    .MinimumLevel.Is(config.CentralMinLevel)
                    .WriteTo.Async(a => a.File(
                        path: centralFile,
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: CentralLoggingDefaults.FileSizeLimitBytes,
                        retainedFileCountLimit: Math.Max(1, config.CentralRetentionDays),
                        outputTemplate: CentralLoggingDefaults.OutputTemplate,
                        shared: true)));
            }
            else
            {
                // Probe failed — either the directory could not be created OR a
                // 1-byte test write into it failed (read-only share, no Modify
                // rights, antivirus quarantine, offline files cache, etc.).
                // Without this surface the operator sees an empty central folder
                // with no clue why.
                //
                //   * Local sink: stamp a diagnostic line into <appName>-.log.
                //   * Console (when enabled, e.g. SyncEngine): print the failure
                //     so a manual run shows it immediately.
                //   * Serilog SelfLog: in case Log.* itself is filtered out.
                var reason = probeError?.GetType().Name ?? "Unknown";
                var detail = probeError?.Message ?? "(no exception detail)";
                _centralSinkBootstrapError =
                    $"Central log sink DISABLED — probe failed for '{centralDir}'. " +
                    $"{reason}: {detail}";

                if (config.EnableConsole)
                {
                    try
                    {
                        var prev = Console.ForegroundColor;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Error.WriteLine("[Logging] " + _centralSinkBootstrapError);
                        Console.ForegroundColor = prev;
                    }
                    catch { /* console not available */ }
                }

                try { SelfLog.WriteLine(_centralSinkBootstrapError); } catch { }
            }
        }

        // ─── Console sink (opt-in) ──────────────────────────────────────────
        if (config.EnableConsole)
        {
            cfg = cfg.WriteTo.Console(outputTemplate: CentralLoggingDefaults.OutputTemplate);
        }

        return cfg;
    }

    private static LoggerConfiguration ApplyLocalLevel(
        LoggerConfiguration sub, CentralLoggingConfig config)
    {
        return config.LocalFileLevelSwitch is { } sw
            ? sub.MinimumLevel.ControlledBy(sw)
            : sub.MinimumLevel.Is(config.LocalFileMinLevel);
    }

    private static bool TryCreateDirectory(string path)
    {
        try { Directory.CreateDirectory(path); return true; }
        catch { return false; }
    }

    private static (bool Created, Exception? Error) TryCreateDirectoryDiagnostic(string path)
    {
        try { Directory.CreateDirectory(path); return (true, null); }
        catch (Exception ex) { return (false, ex); }
    }

    /// <summary>
    /// Verifies that the central log directory is both creatable AND writable
    /// by the current process identity. Catches the common "directory exists
    /// but account has no Modify rights" case that <see cref="Directory.CreateDirectory"/>
    /// alone would report as success.
    /// </summary>
    private static (bool Ok, Exception? Error) TryProbeCentralDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }

        var probeFile = Path.Combine(
            path,
            $".sinet-logprobe-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
        try
        {
            // 1-byte write+delete proves we have Modify rights on the folder
            // and the share is actually online (not just an Offline-Files cache).
            File.WriteAllBytes(probeFile, [(byte)'.']);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
        finally
        {
            try { if (File.Exists(probeFile)) File.Delete(probeFile); } catch { }
        }
    }

    /// <summary>
    /// Routes Serilog's internal SelfLog into <see cref="CentralSinkBootstrapError"/>
    /// AND into the local file sink (via Log.Warning) so File-sink runtime errors
    /// (file locked, share dropped mid-run, retention cleanup failure, …) become
    /// visible instead of being swallowed. Idempotent.
    /// </summary>
    private static void EnableSelfLogCapture(CentralLoggingConfig config)
    {
        if (Interlocked.Exchange(ref _selfLogWired, 1) == 1) return;

        SelfLog.Enable(message =>
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            _centralSinkBootstrapError = "Serilog SelfLog: " + message.Trim();

            if (config.EnableConsole)
            {
                try { Console.Error.WriteLine("[Logging] " + _centralSinkBootstrapError); }
                catch { }
            }

            // Best-effort: forward to the live logger so it lands in the local file.
            try { Log.Warning("[Logging] {Detail}", _centralSinkBootstrapError); }
            catch { }
        });
    }

    private static int _selfLogWired;

    /// <summary>
    /// Set when <see cref="AddSiNetCentralLogging"/> failed to create the
    /// per-app/machine/user sub-folder under the central share. Exposed so
    /// callers can re-emit the diagnostic via Log.* once the logger is built
    /// (Serilog can't log to itself during configuration).
    /// </summary>
    public static string? CentralSinkBootstrapError => _centralSinkBootstrapError;

    private static string? _centralSinkBootstrapError;

    /// <summary>
    /// The directory the central sink was configured against — set whether or
    /// not the sink actually came up. Lets host apps log "we tried to write the
    /// central log to X" so an empty central folder is easy to diagnose.
    /// </summary>
    public static string? CentralSinkTargetDirectory => _centralSinkTargetDirectory;

    /// <summary>
    /// The full file path the central sink was configured to write (rolling).
    /// </summary>
    public static string? CentralSinkTargetFile => _centralSinkTargetFile;

    /// <summary>True when the central sink actually came up.</summary>
    public static bool CentralSinkEnabled => _centralSinkBootstrapError is null
                                              && _centralSinkTargetDirectory is not null;

    private static string? _centralSinkTargetDirectory;
    private static string? _centralSinkTargetFile;

    /// <summary>The directory the local file sink writes to.</summary>
    public static string? LocalSinkTargetDirectory => _localSinkTargetDirectory;

    /// <summary>The full file path the local file sink writes (rolling).</summary>
    public static string? LocalSinkTargetFile => _localSinkTargetFile;

    private static string? _localSinkTargetDirectory;
    private static string? _localSinkTargetFile;

    private static string SafeMachineName()
    {
        try { return Environment.MachineName; } catch { return "unknown-machine"; }
    }

    private static string SafeUserName()
    {
        try { return Environment.UserName; } catch { return "unknown-user"; }
    }

    private sealed class ThreadIdEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("ThreadId", Environment.CurrentManagedThreadId));
        }
    }
}

/// <summary>
/// Loads <see cref="CentralLoggingConfig"/> from the SystemSettings table using
/// raw ADO.NET — no EF context needed. This lets the logger boot before the
/// DI container is built.
/// </summary>
public static class CentralLoggingSettings
{
    /// <summary>
    /// Reads logging settings from the SystemSettings table. On any failure
    /// (DB unreachable, missing rows, parse errors) returns the compile-time
    /// defaults so the logger always boots.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string. May be null/empty.</param>
    /// <param name="app">Logical app identifier — selects per-app level keys.</param>
    /// <param name="enableConsole">Forwarded to <see cref="CentralLoggingConfig.EnableConsole"/>.</param>
    /// <param name="localFileLevelSwitch">Optional dynamic level switch for the local file sink (see <see cref="CentralLoggingConfig.LocalFileLevelSwitch"/>).</param>
    public static CentralLoggingConfig LoadFromDatabase(
        string? connectionString,
        SiNetApp app,
        bool enableConsole = false,
        LoggingLevelSwitch? localFileLevelSwitch = null)
    {
        // Per-app default levels — applied when neither the DB nor the caller
        // supply an override. Designed for the central share to be useful but
        // not flooded.
        var (defaultLocal, defaultCentral) = app switch
        {
            // Client: local toggled by switch in-app (Error by default).
            // Central is Warning so lifecycle markers (opened / closing) reach
            // the shared share without flooding it with verbose UI noise.
            SiNetApp.Client     => (LogEventLevel.Error,       LogEventLevel.Warning),
            SiNetApp.AccService => (LogEventLevel.Information, LogEventLevel.Warning),
            SiNetApp.SyncEngine => (LogEventLevel.Information, LogEventLevel.Warning),
            _                   => (LogEventLevel.Information, LogEventLevel.Warning)
        };

        var defaults = new CentralLoggingConfig
        {
            App = app,
            CentralLogPath = CentralLoggingDefaults.DefaultCentralLogPath,
            LocalFileMinLevel = defaultLocal,
            CentralMinLevel = defaultCentral,
            EnableConsole = enableConsole,
            LocalFileLevelSwitch = localFileLevelSwitch
        };

        if (string.IsNullOrWhiteSpace(connectionString))
            return defaults;

        Dictionary<string, string>? rows;
        try
        {
            rows = ReadSettingsRows(connectionString!);
        }
        catch
        {
            // DB unreachable at logger-boot time — fall back to defaults silently.
            return defaults;
        }

        var fileLevelKey = app switch
        {
            SiNetApp.Client     => SystemSettingKeys.LoggingClientFileLevel,
            SiNetApp.AccService => SystemSettingKeys.LoggingAccServiceFileLevel,
            SiNetApp.SyncEngine => SystemSettingKeys.LoggingSyncEngineFileLevel,
            _ => string.Empty
        };
        var centralLevelKey = app switch
        {
            SiNetApp.Client     => SystemSettingKeys.LoggingClientCentralLevel,
            SiNetApp.AccService => SystemSettingKeys.LoggingAccServiceCentralLevel,
            SiNetApp.SyncEngine => SystemSettingKeys.LoggingSyncEngineCentralLevel,
            _ => string.Empty
        };

        return defaults with
        {
            CentralLogPath = TrimToNull(GetRow(rows, SystemSettingKeys.LoggingCentralLogPath))
                              ?? defaults.CentralLogPath,
            LocalFileMinLevel = ParseLevel(GetRow(rows, fileLevelKey), defaults.LocalFileMinLevel),
            CentralMinLevel = ParseLevel(GetRow(rows, centralLevelKey), defaults.CentralMinLevel),
            LocalRetentionDays = ParseInt(GetRow(rows, SystemSettingKeys.LoggingLocalRetentionDays), defaults.LocalRetentionDays),
            CentralRetentionDays = ParseInt(GetRow(rows, SystemSettingKeys.LoggingCentralRetentionDays), defaults.CentralRetentionDays)
        };
    }

    private static Dictionary<string, string> ReadSettingsRows(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT SettingKey, SettingValue FROM dbo.SystemSettings " +
            "WHERE SettingKey LIKE 'Logging.%';";
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = 5; // logger boot must not stall on DB hiccups

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            var value = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            result[key] = value;
        }
        return result;
    }

    private static string? GetRow(Dictionary<string, string>? rows, string key)
        => rows is not null && rows.TryGetValue(key, out var v) ? v : null;

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LogEventLevel ParseLevel(string? value, LogEventLevel fallback)
        => Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var lvl) ? lvl : fallback;

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var n) && n > 0 ? n : fallback;
}
