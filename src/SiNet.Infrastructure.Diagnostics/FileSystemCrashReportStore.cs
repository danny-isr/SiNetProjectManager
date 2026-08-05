using System.Globalization;
using System.Text;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Diagnostics;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Diagnostics;

/// <summary>
/// Writes crash reports to <c>{share}\{MachineName}\</c> and applies retention inside that folder
/// only. Retention is best-effort: a delete failure is logged and reported, never fatal (DEV-010).
/// </summary>
public sealed class FileSystemCrashReportStore : IWorkstationCrashReportStore
{
    private readonly ISystemSettingsQueryService _settings;
    private readonly IAppLogger _logger;

    public FileSystemCrashReportStore(ISystemSettingsQueryService settings, IAppLogger logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> ResolveShareRootAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        return ResolveShareRoot(settings);
    }

    public async Task<CrashReportSaveResult> SaveToShareAsync(
        WorkstationCrashReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        var settings = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        var shareRoot = ResolveShareRoot(settings);

        if (string.IsNullOrWhiteSpace(shareRoot))
        {
            throw new InvalidOperationException(
                "No crash report share is configured. Set Diagnostics.CrashReportSharePath or Logging.CentralLogPath.");
        }

        var machineFolder = Path.Combine(shareRoot, SanitizeFolderName(report.Machine.MachineName));
        Directory.CreateDirectory(machineFolder);

        var csvPath = Path.Combine(
            machineFolder,
            CrashReportFileNames.BuildCsvFileName(
                report.Machine.MachineName, report.GeneratedAt, report.Context.Category));

        var markdownPath = Path.Combine(
            machineFolder,
            CrashReportFileNames.BuildMarkdownFileName(
                report.Machine.MachineName, report.GeneratedAt, report.Context.Category));

        await WriteAsync(csvPath, WorkstationCrashReportFormatter.ToCsv(report), cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(markdownPath, WorkstationCrashReportFormatter.ToMarkdown(report), cancellationToken)
            .ConfigureAwait(false);

        var (deleted, warning) = ApplyRetention(
            machineFolder,
            settings.Diagnostics.CrashReportRetentionDays,
            report.GeneratedAt);

        _logger.Info(string.Create(
            CultureInfo.InvariantCulture,
            $"Crash report saved to {machineFolder} (retention removed {deleted} file(s))."));

        return new CrashReportSaveResult(machineFolder, csvPath, markdownPath, deleted, warning);
    }

    public Task SaveCopyAsync(string fullPath, string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException("A target path is required.", nameof(fullPath));
        }

        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return WriteAsync(fullPath, content, cancellationToken);
    }

    internal static string? ResolveShareRoot(SystemSettingsDto settings)
    {
        var configured = settings.Diagnostics.CrashReportSharePath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var centralLogPath = settings.Logging.CentralLogPath;
        return string.IsNullOrWhiteSpace(centralLogPath)
            ? null
            : Path.Combine(
                centralLogPath.Trim(),
                SystemSettingsDefaults.DiagnosticsCrashReportShareFolderName);
    }

    /// <summary>
    /// Deletes expired reports in one machine folder. Returns the count and, when something could
    /// not be deleted, a warning for the status bar.
    /// </summary>
    internal (int Deleted, string? Warning) ApplyRetention(
        string machineFolder,
        int retentionDays,
        DateTimeOffset now)
    {
        List<CrashReportFileInfo> candidates;

        try
        {
            candidates = Directory
                .EnumerateFiles(machineFolder)
                .Select(path => new CrashReportFileInfo(
                    Path.GetFileName(path),
                    new DateTimeOffset(File.GetLastWriteTime(path))))
                .ToList();
        }
        catch (IOException ex)
        {
            _logger.Warn($"Crash report retention could not list {machineFolder}: {ex.Message}");
            return (0, "לא ניתן היה לסרוק דוחות ישנים לניקוי.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn($"Crash report retention could not list {machineFolder}: {ex.Message}");
            return (0, "לא ניתן היה לסרוק דוחות ישנים לניקוי.");
        }

        var doomed = CrashReportRetentionPlanner.SelectForDeletion(candidates, retentionDays, now);
        var deleted = 0;
        var failures = 0;

        foreach (var file in doomed)
        {
            try
            {
                File.Delete(Path.Combine(machineFolder, file.FileName));
                deleted++;
            }
            catch (IOException ex)
            {
                failures++;
                _logger.Warn($"Crash report retention could not delete {file.FileName}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                failures++;
                _logger.Warn($"Crash report retention could not delete {file.FileName}: {ex.Message}");
            }
        }

        var warning = failures == 0
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"ניקוי דוחות ישנים נכשל עבור {failures} קבצים.");

        return (deleted, warning);
    }

    private static Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);

    private static string SanitizeFolderName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "UNKNOWN" : sanitized;
    }
}
