using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Settings;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email;

public sealed class SqlEmailFilingService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IEmailGmailModifyService gmailModify) : IEmailFilingService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly IEmailGmailModifyService _gmailModify =
        gmailModify ?? throw new ArgumentNullException(nameof(gmailModify));

    public async Task<EmailFilingResult> FileToProjectAsync(
        FileEmailToProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.TargetProjectId <= 0)
        {
            return new EmailFilingResult(false, "Invalid target project.");
        }

        if (string.IsNullOrWhiteSpace(command.GmailMessageId))
        {
            return new EmailFilingResult(false, "Missing Gmail message id.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var project = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == command.TargetProjectId)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.NameAndNumber,
                PlaceTitle = p.Place != null ? p.Place.Title : null,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (project is null)
        {
            return new EmailFilingResult(false, $"Project {command.TargetProjectId} not found.");
        }

        var location = EmailProjectLabelFormatter.GetLocation(project.PlaceTitle);
        var projectDisplayName = EmailProjectLabelFormatter.FormatProjectName(
            project.Id,
            project.NameAndNumber,
            project.Title);

        // Tracks the label we newly attached so we can compensate (remove it) if the SQL sync that
        // follows fails. Without this, a Gmail label applied + a DB failure leaves an orphaned label
        // in Gmail with no matching SQL mapping.
        string? attachedLabelId = null;

        try
        {
            var gmailSw = Stopwatch.StartNew();
            var existingProjectLabelIds = await _gmailModify
                .GetProjectLabelIdsOnMessageAsync(command.GmailMessageId, cancellationToken)
                .ConfigureAwait(false);
            if (existingProjectLabelIds.Count > 0)
            {
                await _gmailModify
                    .RemoveProjectLabelsFromMessageAsync(
                        command.GmailMessageId,
                        existingProjectLabelIds,
                        moveToInbox: false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var labelId = await _gmailModify
                .GetOrCreateProjectLabelAsync(location, projectDisplayName, cancellationToken)
                .ConfigureAwait(false);
            await _gmailModify
                .AttachProjectLabelAsync(command.GmailMessageId, labelId, cancellationToken)
                .ConfigureAwait(false);
            attachedLabelId = labelId;
            var gmailMs = gmailSw.ElapsedMilliseconds;

            var dbSw = Stopwatch.StartNew();
            await TrySyncSqlAfterFileAsync(
                db,
                command,
                project.Id,
                cancellationToken).ConfigureAwait(false);
            var dbMs = dbSw.ElapsedMilliseconds;

            Debug.WriteLine($"[PERF] EmailFiling FileToProject gmail={gmailMs}ms db={dbMs}ms");

            return new EmailFilingResult(true, AssignedProjectId: command.TargetProjectId);
        }
        catch (Exception ex)
        {
            // The SQL sync failed after the Gmail label was applied. Best-effort compensation:
            // remove the just-attached label so Gmail and SQL end in a consistent "not filed" state
            // (matching the rolled-back DB). We cannot reliably restore any project labels that were
            // removed in the reassign step above, so the message settles on "no project label".
            if (!string.IsNullOrWhiteSpace(attachedLabelId))
            {
                try
                {
                    await _gmailModify
                        .RemoveProjectLabelAsync(command.GmailMessageId, attachedLabelId, moveToInbox: false, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception compensationEx)
                {
                    // Never mask the original error; the compensation is best-effort.
                    Debug.WriteLine($"[EmailFiling] Compensation RemoveProjectLabel failed: {compensationEx.Message}");
                }
            }

            return new EmailFilingResult(false, ex.Message);
        }
    }

    public async Task<EmailFilingResult> UnfileFromProjectAsync(
        UnfileEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.GmailMessageId))
        {
            return new EmailFilingResult(false, "Missing Gmail message id.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var gmailSw = Stopwatch.StartNew();
            var labelId = await ResolveProjectLabelIdForUnfileAsync(command, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(labelId))
            {
                await _gmailModify
                    .RemoveProjectLabelAsync(command.GmailMessageId, labelId, moveToInbox: true, cancellationToken)
                    .ConfigureAwait(false);
            }

            var gmailMs = gmailSw.ElapsedMilliseconds;

            var dbSw = Stopwatch.StartNew();
            await TrySyncSqlAfterUnfileAsync(db, command, cancellationToken).ConfigureAwait(false);
            var dbMs = dbSw.ElapsedMilliseconds;

            Debug.WriteLine($"[PERF] EmailFiling UnfileFromProject gmail={gmailMs}ms db={dbMs}ms");

            return new EmailFilingResult(true);
        }
        catch (Exception ex)
        {
            return new EmailFilingResult(false, ex.Message);
        }
    }

    private async Task<string?> ResolveProjectLabelIdForUnfileAsync(
        UnfileEmailCommand command,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.ProjectLabelFullPath))
        {
            return await _gmailModify
                .GetProjectLabelIdByFullPathAsync(command.ProjectLabelFullPath, cancellationToken)
                .ConfigureAwait(false);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (command.InboxMessageId is > 0)
        {
            var inbox = await db.EmailInboxMessages
                .AsNoTracking()
                .Where(message => message.Id == command.InboxMessageId.Value)
                .Select(message => new { message.ProjectId })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (inbox?.ProjectId is > 0)
            {
                var project = await db.Projects
                    .AsNoTracking()
                    .Where(p => p.Id == inbox.ProjectId)
                    .Select(p => new
                    {
                        p.Id,
                        p.NameAndNumber,
                        p.Title,
                        PlaceTitle = p.Place != null ? p.Place.Title : null,
                    })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (project is not null)
                {
                    var location = EmailProjectLabelFormatter.GetLocation(project.PlaceTitle);
                    var projectDisplayName = EmailProjectLabelFormatter.FormatProjectName(
                        inbox.ProjectId,
                        project.NameAndNumber,
                        project.Title);
                    return await _gmailModify
                        .GetProjectLabelIdAsync(location, projectDisplayName, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        var projectLabelIds = await _gmailModify
            .GetProjectLabelIdsOnMessageAsync(command.GmailMessageId, cancellationToken)
            .ConfigureAwait(false);

        return projectLabelIds.FirstOrDefault();
    }

    private async Task TrySyncSqlAfterFileAsync(
        SiNetSQLDbContext db,
        FileEmailToProjectCommand command,
        int targetProjectId,
        CancellationToken cancellationToken)
    {
        // Keep the thread-mapping upsert and the inbox-row update atomic with each other so a
        // partial "mapping saved but inbox not updated" state can never persist. Transactions are a
        // relational-only feature (the EF InMemory provider used by unit tests does not support
        // them), so only open one when the underlying provider is relational.
        var useTransaction = db.Database.IsRelational();
        await using var transaction = useTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var inbox = await ResolveInboxRowAsync(db, command.InboxMessageId, command.GmailMessageId, command.InternetMessageId, cancellationToken)
            .ConfigureAwait(false);

        var gmailThreadId = command.GmailThreadId ?? inbox?.GmailThreadId;
        if (!string.IsNullOrWhiteSpace(gmailThreadId) && !string.IsNullOrWhiteSpace(inbox?.ThreadUniqueId))
        {
            var mapping = await db.ThreadStatusMappings
                .FirstOrDefaultAsync(m => m.ThreadUniqueId == inbox.ThreadUniqueId, cancellationToken)
                .ConfigureAwait(false);

            if (mapping is null)
            {
                db.ThreadStatusMappings.Add(new ThreadStatusMapping
                {
                    ThreadUniqueId = inbox.ThreadUniqueId,
                    ThreadId = gmailThreadId,
                    ProjectId = targetProjectId,
                    Status = ThreadMappingStatus.Assigned,
                    LastUpdated = DateTime.UtcNow,
                });
            }
            else
            {
                mapping.ProjectId = targetProjectId;
                mapping.Status = ThreadMappingStatus.Assigned;
                mapping.ThreadId = gmailThreadId;
                mapping.LastUpdated = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (inbox is not null)
        {
            await db.EmailInboxMessages
                .Where(message => message.Id == inbox.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(message => message.ProjectId, targetProjectId)
                        .SetProperty(
                            message => message.GmailThreadId,
                            message => message.GmailThreadId ?? gmailThreadId),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TrySyncSqlAfterUnfileAsync(
        SiNetSQLDbContext db,
        UnfileEmailCommand command,
        CancellationToken cancellationToken)
    {
        var inbox = await ResolveInboxRowAsync(db, command.InboxMessageId, command.GmailMessageId, command.InternetMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(inbox?.ThreadUniqueId))
        {
            await ConditionallyCleanThreadMappingAsync(
                db,
                inbox.ThreadUniqueId,
                command.GmailMessageId,
                command.InternetMessageId,
                cancellationToken).ConfigureAwait(false);
        }

        var defaultProjectId = await ResolveDefaultOfficeProjectIdAsync(db, cancellationToken).ConfigureAwait(false);
        if (defaultProjectId <= 0 || inbox is null)
        {
            return;
        }

        await db.EmailInboxMessages
            .Where(message => message.Id == inbox.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(message => message.ProjectId, defaultProjectId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<InboxRow?> ResolveInboxRowAsync(
        SiNetSQLDbContext db,
        int? inboxMessageId,
        string gmailMessageId,
        string? internetMessageId,
        CancellationToken cancellationToken)
    {
        if (inboxMessageId is > 0)
        {
            return await db.EmailInboxMessages
                .AsNoTracking()
                .Where(message => message.Id == inboxMessageId.Value)
                .Select(message => new InboxRow(message.Id, message.ThreadUniqueId, message.GmailThreadId))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var messageUniqueId = EmailMessageIdentity.GetMessageUniqueId(internetMessageId, gmailMessageId);
        return await db.EmailInboxMessages
            .AsNoTracking()
            .Where(message => message.MessageUniqueId == messageUniqueId)
            .Select(message => new InboxRow(message.Id, message.ThreadUniqueId, message.GmailThreadId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<int> ResolveDefaultOfficeProjectIdAsync(
        SiNetSQLDbContext db,
        CancellationToken cancellationToken)
    {
        var defaultTitle = await db.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.SettingKey == SystemSettingKeys.DefaultProjectTitle)
            .Select(setting => setting.SettingValue)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(defaultTitle))
        {
            defaultTitle = SystemSettingsDefaults.DefaultProjectTitle;
        }

        return await db.Projects
            .AsNoTracking()
            .Where(project => project.Title == defaultTitle)
            .Select(project => project.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ConditionallyCleanThreadMappingAsync(
        SiNetSQLDbContext db,
        string threadUniqueId,
        string gmailMessageId,
        string? internetMessageId,
        CancellationToken cancellationToken)
    {
        var excludeUniqueId = EmailMessageIdentity.GetMessageUniqueId(internetMessageId, gmailMessageId);
        var defaultProjectId = await ResolveDefaultOfficeProjectIdAsync(db, cancellationToken).ConfigureAwait(false);

        var siblingsStillFiled = await db.EmailInboxMessages
            .AsNoTracking()
            .Where(message =>
                message.ThreadUniqueId == threadUniqueId
                && message.MessageUniqueId != excludeUniqueId
                && message.ProjectId != defaultProjectId
                && message.ProjectId != null)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!siblingsStillFiled)
        {
            await db.ThreadStatusMappings
                .Where(mapping => mapping.ThreadUniqueId == threadUniqueId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed record InboxRow(int Id, string? ThreadUniqueId, string? GmailThreadId);
}
