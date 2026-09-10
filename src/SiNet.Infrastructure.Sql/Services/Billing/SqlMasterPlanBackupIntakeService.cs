using Microsoft.EntityFrameworkCore;
using SiNet.Application.MasterPlanBackup;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>
/// Desktop intake: COPY user bak into Incoming via *.partial then rename. Never RESTORE. Never N:\.
/// </summary>
public sealed class SqlMasterPlanBackupIntakeService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    TimeProvider? timeProvider = null,
    string? inboxRoot = null) : IMasterPlanBackupIntakeService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly string _inboxRoot = string.IsNullOrWhiteSpace(inboxRoot)
        ? MasterPlanBackupIncomingCopy.DefaultInboxRoot
        : inboxRoot;

    public async Task<MasterPlanBackupIntakeAcceptResult> AcceptAsync(
        string sourcePath,
        MasterPlanBackupIntakeSource sourceType,
        string receivedBy,
        string? gmailMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(receivedBy);

        if (sourcePath.StartsWith(@"N:\", StringComparison.OrdinalIgnoreCase)
            || sourcePath.StartsWith("N:/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("אין לקבל נתיב N:\\ כמקור גיבוי.");
        }

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("קובץ הגיבוי לא נמצא.", sourcePath);

        if (!Path.GetExtension(sourcePath).Equals(".bak", StringComparison.OrdinalIgnoreCase)
            || MasterPlanBackupIncomingCopy.IsPartialFile(sourcePath))
        {
            throw new InvalidOperationException("רק קובץ .bak שהועתק במלואו מתקבל לתור הגיבוי.");
        }

        var sourceSha = await MasterPlanBackupIncomingCopy.ComputeSha256Async(sourcePath, cancellationToken)
            .ConfigureAwait(false);
        var existing = await GetBySha256Async(sourceSha, cancellationToken).ConfigureAwait(false);
        if (existing is not null
            && existing.Status is MasterPlanBackupIntakeStatus.Queued
                or MasterPlanBackupIntakeStatus.Processing
                or MasterPlanBackupIntakeStatus.Restored
                or MasterPlanBackupIntakeStatus.SkippedNotNewer)
        {
            return new MasterPlanBackupIntakeAcceptResult(existing, Duplicate: true);
        }

        var originalName = Path.GetFileName(sourcePath);
        var destName =
            $"{_time.GetUtcNow().UtcDateTime:yyyyMMddHHmmss}_{Sanitize(originalName)}";
        var (destBak, sha) = await MasterPlanBackupIncomingCopy.CopyAtomicallyAsync(
                sourcePath,
                _inboxRoot,
                destName,
                cancellationToken)
            .ConfigureAwait(false);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = new MasterPlanBackupIntake
        {
            SourceType = (int)sourceType,
            GmailMessageId = gmailMessageId,
            OriginalFileName = originalName,
            IncomingPath = destBak,
            Sha256 = sha,
            ReceivedAtUtc = _time.GetUtcNow().UtcDateTime,
            ReceivedBy = receivedBy.Trim(),
            Status = (int)MasterPlanBackupIntakeStatus.Queued
        };
        db.MasterPlanBackupIntakes.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new MasterPlanBackupIntakeAcceptResult(Map(entity), Duplicate: false);
    }

    public async Task<MasterPlanBackupIntakeRecord?> GetByIncomingPathAsync(
        string incomingPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incomingPath);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.MasterPlanBackupIntakes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IncomingPath == incomingPath, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<MasterPlanBackupIntakeRecord?> GetBySha256Async(
        string sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.MasterPlanBackupIntakes
            .AsNoTracking()
            .Where(r => r.Sha256 == sha256)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    private static string Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static MasterPlanBackupIntakeRecord Map(MasterPlanBackupIntake row) =>
        new(
            row.Id,
            (MasterPlanBackupIntakeSource)row.SourceType,
            row.GmailMessageId,
            row.OriginalFileName,
            row.IncomingPath,
            row.Sha256,
            row.ReceivedAtUtc,
            row.ReceivedBy,
            row.BackupFinishDate,
            (MasterPlanBackupIntakeStatus)row.Status,
            row.ResultMessage,
            row.ProcessedAtUtc);
}
