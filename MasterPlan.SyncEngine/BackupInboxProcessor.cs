using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Claims one Incoming .bak (never *.partial), restores through the existing monthly pipeline
/// without allow-older, then moves the file to Processed/Rejected. Desktop never RESTORE.
/// </summary>
public sealed record BackupInboxIntakeUpdate(int Status, string Message, string Path);

public static class BackupInboxProcessor
{
    public const string IncomingFolderName = "Incoming";
    public const string ProcessingFolderName = "Processing";
    public const string ProcessedFolderName = "Processed";
    public const string RejectedFolderName = "Rejected";
    public const string PartialExtension = ".partial";

    public static bool IsPartialFile(string path) =>
        Path.GetExtension(path).Equals(PartialExtension, StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".bak" + PartialExtension, StringComparison.OrdinalIgnoreCase);

    public static bool IsCompletedBak(string path) =>
        Path.GetExtension(path).Equals(".bak", StringComparison.OrdinalIgnoreCase)
        && !IsPartialFile(path);

    public static void EnsureLayout(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Directory.CreateDirectory(Path.Combine(root, IncomingFolderName));
        Directory.CreateDirectory(Path.Combine(root, ProcessingFolderName));
        Directory.CreateDirectory(Path.Combine(root, ProcessedFolderName));
        Directory.CreateDirectory(Path.Combine(root, RejectedFolderName));
    }

    public static IReadOnlyList<string> ListClaimableBaks(string folder)
    {
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder, "*.bak", SearchOption.TopDirectoryOnly)
            .Where(IsCompletedBak)
            .OrderBy(File.GetLastWriteTimeUtc)
            .ToList();
    }

    public static string ToServerRestorePath(string clientFilePath, MonthlyBackupStagingOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientFilePath);
        ArgumentNullException.ThrowIfNull(options);

        var clientRoot = MonthlyBackupStaging.NormalizeDirectory(options.ClientStagingPath);
        var serverRoot = MonthlyBackupStaging.NormalizeDirectory(options.ServerStagingPath);
        var full = Path.GetFullPath(clientFilePath);
        var relative = Path.GetRelativePath(clientRoot, full);
        if (relative.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            return Path.Combine(serverRoot, ProcessingFolderName, Path.GetFileName(full));
        }

        return Path.Combine(serverRoot, relative);
    }

    public static Task<int> ProcessOneAsync(
        MonthlyBackupStagingOptions staging,
        MonthlyBackupRestoreService restore,
        string? siDataConnectionString,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restore);
        return ProcessOneAsync(
            staging,
            (serverPath, _) => restore.RunMonthlyBackupRestoreAsync(serverPath, allowOlderOrEqualBackup: false),
            siDataConnectionString,
            logger,
            cancellationToken);
    }

    public static async Task<int> ProcessOneAsync(
        MonthlyBackupStagingOptions staging,
        Func<string, CancellationToken, Task<MonthlyBackupResult>> restore,
        string? siDataConnectionString,
        ILogger logger,
        CancellationToken cancellationToken = default,
        IList<BackupInboxIntakeUpdate>? intakeUpdates = null)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(logger);

        if (staging.ClientStagingPath.StartsWith(@"N:\", StringComparison.OrdinalIgnoreCase)
            || staging.ServerStagingPath.StartsWith(@"N:\", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("נתיב N:\\ אינו חוקי לתור גיבוי MasterPlan.");
        }

        EnsureLayout(staging.ClientStagingPath);
        var incoming = Path.Combine(staging.ClientStagingPath, IncomingFolderName);
        var processing = Path.Combine(staging.ClientStagingPath, ProcessingFolderName);
        var processed = Path.Combine(staging.ClientStagingPath, ProcessedFolderName);
        var rejected = Path.Combine(staging.ClientStagingPath, RejectedFolderName);

        var claimed = ListClaimableBaks(processing).FirstOrDefault()
                      ?? ClaimNext(incoming, processing);
        if (claimed is null)
        {
            logger.LogInformation("Backup inbox is empty (Incoming and Processing).");
            return 0;
        }

        await TryUpdateIntakeAsync(
                siDataConnectionString,
                claimed,
                status: 2,
                message: "ממתין לעיבוד",
                processedAt: null,
                cancellationToken,
                intakeUpdates)
            .ConfigureAwait(false);

        var serverPath = ToServerRestorePath(claimed, staging);
        logger.LogWarning(
            "Backup inbox processing {Client} via SQL path {Server}. allow-older=false",
            claimed,
            serverPath);

        try
        {
            var result = await restore(serverPath, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "שחזור חודשי נכשל.");
            }

            var dest = Path.Combine(processed, Path.GetFileName(claimed));
            MoveReplace(claimed, dest);
            await TryUpdateIntakeAsync(
                    siDataConnectionString,
                    dest,
                    status: 3,
                    message: "שוחזר בהצלחה",
                    processedAt: DateTime.UtcNow,
                    cancellationToken,
                    intakeUpdates)
                .ConfigureAwait(false);
            return 0;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("אינו חדש", StringComparison.Ordinal))
        {
            var dest = Path.Combine(rejected, Path.GetFileName(claimed));
            MoveReplace(claimed, dest);
            await TryUpdateIntakeAsync(
                    siDataConnectionString,
                    dest,
                    status: 4,
                    message: ex.Message,
                    processedAt: DateTime.UtcNow,
                    cancellationToken,
                    intakeUpdates)
                .ConfigureAwait(false);
            logger.LogWarning(ex, "Backup inbox skipped — not newer than last monthly restore.");
            return 0;
        }
        catch (Exception ex)
        {
            var dest = Path.Combine(rejected, Path.GetFileName(claimed));
            if (File.Exists(claimed))
                MoveReplace(claimed, dest);
            await TryUpdateIntakeAsync(
                    siDataConnectionString,
                    dest,
                    status: 5,
                    message: ex.Message,
                    processedAt: DateTime.UtcNow,
                    cancellationToken,
                    intakeUpdates)
                .ConfigureAwait(false);
            logger.LogError(ex, "Backup inbox restore failed.");
            return 1;
        }
    }

    private static string? ClaimNext(string incoming, string processing)
    {
        var next = ListClaimableBaks(incoming).FirstOrDefault();
        if (next is null)
            return null;

        var dest = Path.Combine(processing, Path.GetFileName(next));
        MoveReplace(next, dest);
        return dest;
    }

    private static void MoveReplace(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (File.Exists(dest))
            File.Delete(dest);
        File.Move(source, dest);
    }

    private static async Task TryUpdateIntakeAsync(
        string? siDataConnectionString,
        string path,
        int status,
        string message,
        DateTime? processedAt,
        CancellationToken cancellationToken,
        IList<BackupInboxIntakeUpdate>? intakeUpdates)
    {
        intakeUpdates?.Add(new BackupInboxIntakeUpdate(status, message, path));
        if (string.IsNullOrWhiteSpace(siDataConnectionString))
            return;

        try
        {
            await using var conn = new SqlConnection(siDataConnectionString);
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            const string sql =
                """
                IF OBJECT_ID(N'dbo.MasterPlanBackupIntake', N'U') IS NULL
                    RETURN;
                UPDATE dbo.MasterPlanBackupIntake
                SET Status = @Status,
                    ResultMessage = @Message,
                    ProcessedAtUtc = COALESCE(@ProcessedAt, ProcessedAtUtc),
                    IncomingPath = @Path
                WHERE IncomingPath = @Path
                   OR OriginalFileName = @FileName
                   OR IncomingPath LIKE N'%' + @FileName;
                """;
            await conn.ExecuteAsync(new CommandDefinition(
                    sql,
                    new
                    {
                        Status = status,
                        Message = message,
                        ProcessedAt = processedAt,
                        Path = path,
                        FileName = Path.GetFileName(path)
                    },
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }
        catch (SqlException)
        {
            // Metadata is helper-only; restore outcome still stands.
        }
    }
}
