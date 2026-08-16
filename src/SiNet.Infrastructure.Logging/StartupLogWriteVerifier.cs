using System.Text;
using SiNet.Application.Runtime;

namespace SiNet.Infrastructure.Logging;

/// <summary>
/// DEV-028: proves a unique Warning marker was written to today's Client log file(s).
/// </summary>
public static class StartupLogWriteVerifier
{
    private static readonly object Gate = new();
    private static StartupLogWriteVerificationResult? _last;

    /// <summary>Last verify from startup (or a later status refresh).</summary>
    public static StartupLogWriteVerificationResult? LastResult
    {
        get
        {
            lock (Gate)
                return _last;
        }
    }

    /// <summary>
    /// Emits <c>[STARTUP] Client process alive pid=…</c>, flushes the Serilog pipeline, then
    /// read-backs today's local and (when configured) central dated files.
    /// </summary>
    public static StartupLogWriteVerificationResult Verify(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var bound = timeout ?? TimeSpan.FromSeconds(5);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(bound);

        var marker = $"[STARTUP] Client process alive pid={Environment.ProcessId}";
        Serilog.Log.Warning(marker);

        // Flush Async local buffer + sync central File: CloseAndFlush then rebuild same config.
        StandaloneHostLoggingBootstrap.FlushPipeline();

        cts.Token.ThrowIfCancellationRequested();

        var localDir = CentralLoggingBuilder.LocalSinkTargetDirectory;
        var centralDir = CentralLoggingBuilder.CentralSinkTargetDirectory;
        var centralConfigured = CentralLoggingBuilder.CentralSinkEnabled
            && !string.IsNullOrWhiteSpace(centralDir);

        var localPath = ResolveTodayLogPath(localDir, "Client");
        var centralPath = centralConfigured ? ResolveTodayLogPath(centralDir, "Client") : null;

        var localOk = FileContainsMarker(localPath, marker, cts.Token);
        var centralOk = centralConfigured && FileContainsMarker(centralPath, marker, cts.Token);

        string? detail = null;
        if (!localOk)
            detail = localPath is null ? "local path unknown" : $"marker missing in {localPath}";
        else if (centralConfigured && !centralOk)
            detail = centralPath is null
                ? "central path unknown"
                : File.Exists(centralPath)
                    ? $"marker missing in {centralPath}"
                    : $"central file missing: {centralPath}";
        else if (CentralLoggingBuilder.CentralSinkBootstrapError is { } probeErr)
            detail = probeErr;

        var result = new StartupLogWriteVerificationResult(
            marker,
            localOk,
            centralConfigured,
            centralOk,
            localPath,
            centralPath,
            detail,
            DateTimeOffset.UtcNow);

        lock (Gate)
            _last = result;

        return result;
    }

    /// <summary>Async wrapper for splash / status (I/O off UI thread).</summary>
    public static Task<StartupLogWriteVerificationResult> VerifyAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Verify(timeout, cancellationToken), cancellationToken);

    internal static string? ResolveTodayLogPath(string? directory, string appName)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        var dated = Path.Combine(directory, $"{appName}-{DateTime.Now:yyyyMMdd}.log");
        if (File.Exists(dated))
            return dated;

        // Rolling template may still be opening; also accept any Client-*.log from today.
        try
        {
            if (!Directory.Exists(directory))
                return dated;

            var match = Directory.EnumerateFiles(directory, $"{appName}-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            return match ?? dated;
        }
        catch (IOException)
        {
            return dated;
        }
        catch (UnauthorizedAccessException)
        {
            return dated;
        }
    }

    internal static bool FileContainsMarker(string? path, string marker, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            return text.Contains(marker, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// «מצב מערכת» row for central Client Llog write-back (DEV-028).
/// </summary>
public sealed class LoggingCentralStatusContributor : ISubsystemStatusContributor
{
    public string Key => "logging-central";

    public string DisplayNameHe => "לוג מרכזי (Llog)";

    public SubsystemProbeTier Tier => SubsystemProbeTier.Fast;

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        StartupLogWriteVerificationResult result;
        try
        {
            result = await StartupLogWriteVerifier
                .VerifyAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Row(
                SubsystemRuntimeState.Degraded,
                "בדיקת כתיבת לוג — תם הזמן",
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return Row(
                SubsystemRuntimeState.Degraded,
                $"בדיקת כתיבת לוג נכשלה: {ex.GetType().Name}",
                DateTimeOffset.UtcNow);
        }

        if (!result.CentralConfigured)
        {
            return Row(
                result.LocalOk ? SubsystemRuntimeState.NotConfigured : SubsystemRuntimeState.Stopped,
                result.LocalOk
                    ? "לוג מרכזי לא מוגדר (CentralLogPath ריק)"
                    : $"לוג מקומי לא נכתב — {result.LocalPath ?? "(אין נתיב)"}",
                result.CheckedUtc);
        }

        if (result.CentralOk && result.LocalOk)
        {
            return Row(
                SubsystemRuntimeState.Idle,
                $"נכתב — {result.CentralPath}",
                result.CheckedUtc);
        }

        if (!result.LocalOk)
        {
            return Row(
                SubsystemRuntimeState.Stopped,
                $"לוג מקומי לא נמצא — {result.LocalPath ?? "(אין נתיב)"}",
                result.CheckedUtc);
        }

        return Row(
            SubsystemRuntimeState.Degraded,
            result.Detail ?? $"לא נמצא הקובץ — {result.CentralPath}",
            result.CheckedUtc);
    }

    private SubsystemRuntimeStatus Row(
        SubsystemRuntimeState state,
        string summaryHe,
        DateTimeOffset checkedUtc) =>
        new(Key, DisplayNameHe, state, null, summaryHe, checkedUtc);
}
