using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Ensures the explicit PRP Gmail source is fully ingested through production ACC inbox seams
/// before <see cref="IEmailSuggestedActionExecutionService"/> / CreatePriceQuote may run.
/// </summary>
internal static class SystemCertificationPrpSourceIngest
{
    internal sealed record SqlAttachmentSnapshot(
        int Id,
        string FileName,
        string? AccItemId);

    internal static async Task<SystemCertificationPrpCorridorSupport.CorridorInbox?> TryEnsureFullyIngestedAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationHost.SystemCertificationRunContext context,
        int proposalDefinitionId,
        EmailMessageDetails gmailDetails,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(gmailDetails);
        ArgumentNullException.ThrowIfNull(evidence);

        if (!context.Acc.IsEnabled || context.Acc.Violation is not null)
        {
            evidence.Fail(
                "cert.prp.source_ingest",
                "PRP source ingest requires a valid ACC layer and disposable inbox configuration.");
            return null;
        }

        var existingInboxId = await FindInboxMessageIdAsync(dbFactory, gmailDetails, cancellationToken);
        if (existingInboxId is int existing
            && await HasActiveProposalForInboxAsync(
                dbFactory, proposalDefinitionId, existing, cancellationToken))
        {
            evidence.Fail(
                "cert.prp.source_ingest",
                $"Explicit source inbox id={existing} already has an active Proposal instance — "
                + "FAIL BEFORE WORKFLOW START.");
            return null;
        }

        var actingLogin = await ResolveActingUserLoginAsync(
            dbFactory, context.OperatorUserId, evidence, cancellationToken);
        if (actingLogin is null)
        {
            return null;
        }

        var alreadyFullyIngested = existingInboxId is int knownInboxId
            && await IsFullyIngestedAsync(
                provider,
                dbFactory,
                knownInboxId,
                gmailDetails,
                evidence,
                cancellationToken,
                failEvidence: false);

        int inboxId;
        if (alreadyFullyIngested)
        {
            inboxId = existingInboxId!.Value;
            evidence.Pass(
                "cert.prp.source_ingest",
                $"Source already ingested as inbox id={inboxId}; production IngestToInboxAsync skipped.");
        }
        else
        {
            var ingested = await RunProductionIngestAsync(
                provider,
                dbFactory,
                context,
                gmailDetails,
                actingLogin,
                evidence,
                cancellationToken);
            if (ingested is null)
            {
                return null;
            }

            inboxId = ingested.Value;
            evidence.Pass(
                "cert.prp.source_ingest",
                $"Production IEmailAccIngestionExecutor.IngestToInboxAsync completed for inbox id={inboxId}.");
        }

        if (!await VerifySqlReadBackAsync(
                provider,
                dbFactory,
                inboxId,
                gmailDetails,
                evidence,
                cancellationToken))
        {
            return null;
        }

        if (!await VerifyAccInboxReadBackAsync(
                provider,
                dbFactory,
                inboxId,
                evidence,
                cancellationToken))
        {
            return null;
        }

        var detail =
            $"fully ingested inbox id={inboxId} gmail={gmailDetails.MessageId} "
            + $"subject='{TrimSubject(gmailDetails.Subject)}' attachments={gmailDetails.Attachments.Count}";
        evidence.Pass("cert.prp.inbox", detail);
        return new SystemCertificationPrpCorridorSupport.CorridorInbox(inboxId, null, detail);
    }

    private static async Task<int?> RunProductionIngestAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationHost.SystemCertificationRunContext context,
        EmailMessageDetails gmailDetails,
        string actingLogin,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        var bootstrap = provider.GetRequiredService<IAccInboxBootstrapService>();
        AccInboxBootstrapResult inboxBootstrap;
        try
        {
            inboxBootstrap = await bootstrap.EnsureAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            evidence.Fail(
                "cert.prp.source_ingest",
                $"IAccInboxBootstrapService.EnsureAsync failed: {ex.Message}");
            return null;
        }

        context.AccGuard?.Allow(
            inboxBootstrap.AccProjectId,
            $"[SYS-CERT] disposable ACC inbox '{context.Acc.InboxProjectName}' before source ingest");

        var executor = provider.GetRequiredService<IEmailAccIngestionExecutor>();
        var result = await executor.IngestToInboxAsync(
            new EmailAccUploadCommand(
                gmailDetails.MessageId,
                gmailDetails.ThreadId,
                gmailDetails.InternetMessageId,
                actingLogin,
                AllowZeroAttachmentIngest: false),
            cancellationToken);

        if (!result.Succeeded || result.InboxMessageId is not int inboxMessageId || inboxMessageId <= 0)
        {
            evidence.Fail(
                "cert.prp.source_ingest",
                $"IEmailAccIngestionExecutor.IngestToInboxAsync failed ({result.Outcome}): "
                + $"{result.ErrorMessage ?? "(no message)"} — FAIL BEFORE WORKFLOW START.");
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.EmailInboxMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == inboxMessageId, cancellationToken);
        if (row is null)
        {
            evidence.Fail(
                "cert.prp.source_ingest",
                $"Ingest reported inbox id={inboxMessageId} but EmailInboxMessage row is missing.");
            return null;
        }

        if (!string.Equals(row.InboxAccProjectId, inboxBootstrap.AccProjectId, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Fail(
                "cert.prp.source_ingest",
                $"Ingested inbox accProject '{row.InboxAccProjectId ?? "<null>"}' != disposable inbox "
                + $"'{inboxBootstrap.AccProjectId}'.");
            return null;
        }

        return inboxMessageId;
    }

    private static async Task<bool> IsFullyIngestedAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int inboxMessageId,
        EmailMessageDetails gmailDetails,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken,
        bool failEvidence)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.EmailInboxMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == inboxMessageId, cancellationToken);
        if (row is null)
        {
            if (failEvidence)
            {
                evidence.Fail("cert.prp.source_ingest_sql_readback", $"EmailInboxMessage id={inboxMessageId} missing.");
            }

            return false;
        }

        var sqlAttachments = await LoadSqlAttachmentsAsync(dbFactory, inboxMessageId, cancellationToken);
        if (sqlAttachments.Count == 0)
        {
            return false;
        }

        if (!SqlAttachmentsMatchGmailIdentity(gmailDetails, sqlAttachments))
        {
            if (failEvidence)
            {
                evidence.Fail(
                    "cert.prp.source_ingest_sql_readback",
                    "SQL attachment filenames/count do not match the explicit Gmail source.");
            }

            return false;
        }

        var tagging = provider.GetRequiredService<IEmailAttachmentTaggingService>();
        var tagStates = await tagging.LoadInboxAttachmentsAsync(inboxMessageId, cancellationToken);
        if (tagStates.Count(a => a.IsTaggable) == 0)
        {
            if (failEvidence)
            {
                evidence.Fail(
                    "cert.prp.source_ingest_sql_readback",
                    $"Inbox id={inboxMessageId} has SQL attachments but none are taggable through "
                    + "IEmailAttachmentTaggingService.");
            }

            return false;
        }

        if (string.IsNullOrWhiteSpace(row.InboxAccProjectId) || string.IsNullOrWhiteSpace(row.InboxAccFolderId))
        {
            return false;
        }

        return true;
    }

    private static async Task<bool> VerifySqlReadBackAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int inboxMessageId,
        EmailMessageDetails gmailDetails,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        if (!await IsFullyIngestedAsync(
                provider,
                dbFactory,
                inboxMessageId,
                gmailDetails,
                evidence,
                cancellationToken,
                failEvidence: true))
        {
            return false;
        }

        var sqlAttachments = await LoadSqlAttachmentsAsync(dbFactory, inboxMessageId, cancellationToken);
        var taggable = await provider.GetRequiredService<IEmailAttachmentTaggingService>()
            .LoadInboxAttachmentsAsync(inboxMessageId, cancellationToken);

        evidence.Pass(
            "cert.prp.source_ingest_sql_readback",
            $"EmailInboxMessage id={inboxMessageId} exists with {sqlAttachments.Count} SQL attachment(s), "
            + $"{taggable.Count(a => a.IsTaggable)} taggable, matching {gmailDetails.Attachments.Count} "
            + "Gmail attachment identity.");
        return true;
    }

    private static async Task<bool> VerifyAccInboxReadBackAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int inboxMessageId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.EmailInboxMessages.AsNoTracking()
            .FirstAsync(m => m.Id == inboxMessageId, cancellationToken);

        if (string.IsNullOrWhiteSpace(row.InboxAccProjectId) || string.IsNullOrWhiteSpace(row.InboxAccFolderId))
        {
            evidence.Fail(
                "cert.prp.source_ingest_acc_readback",
                $"Inbox id={inboxMessageId} has no InboxAccProjectId/InboxAccFolderId for ACC read-back.");
            return false;
        }

        var sqlAttachments = await LoadSqlAttachmentsAsync(dbFactory, inboxMessageId, cancellationToken);
        var browser = provider.GetRequiredService<IAccFolderBrowserService>();
        var browse = await browser.BrowseAsync(
            row.InboxAccProjectId,
            row.InboxAccFolderId,
            cancellationToken);
        if (browse is null)
        {
            evidence.Fail(
                "cert.prp.source_ingest_acc_readback",
                $"IAccFolderBrowserService.BrowseAsync returned null for inbox project "
                + $"'{row.InboxAccProjectId}' folder '{row.InboxAccFolderId}'.");
            return false;
        }

        var items = browse.Entries.Where(e => e.Kind == AccFolderEntryKind.Item).ToList();
        var failures = new List<string>();
        foreach (var attachment in sqlAttachments)
        {
            var matches = items.Where(i =>
                    string.Equals(i.DisplayName, attachment.FileName, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(attachment.AccItemId)
                        && string.Equals(i.Id, attachment.AccItemId, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matches.Count == 0)
            {
                failures.Add($"missing '{attachment.FileName}'");
                continue;
            }

            if (matches[0].FileSize <= 0)
            {
                failures.Add($"item '{attachment.FileName}' has no file size metadata");
            }
        }

        if (failures.Count > 0)
        {
            evidence.Fail(
                "cert.prp.source_ingest_acc_readback",
                "ACC Inbox read-back failed before CreatePriceQuote: " + string.Join("; ", failures));
            return false;
        }

        evidence.Pass(
            "cert.prp.source_ingest_acc_readback",
            $"ACC Inbox project '{row.InboxAccProjectId}' folder '{row.InboxAccFolderId}' contains "
            + $"{sqlAttachments.Count} expected file(s) with size metadata.");
        return true;
    }

    internal static bool SqlAttachmentsMatchGmailIdentity(
        EmailMessageDetails gmailDetails,
        IReadOnlyList<SqlAttachmentSnapshot> sqlAttachments)
    {
        if (gmailDetails.Attachments.Count == 0 || sqlAttachments.Count < gmailDetails.Attachments.Count)
        {
            return false;
        }

        foreach (var gmailAttachment in gmailDetails.Attachments)
        {
            if (!sqlAttachments.Any(sql =>
                    string.Equals(sql.FileName, gmailAttachment.FileName, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<List<SqlAttachmentSnapshot>> LoadSqlAttachmentsAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int inboxMessageId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.EmailInboxAttachments.AsNoTracking()
            .Where(a => a.MessageId == inboxMessageId)
            .Select(a => new SqlAttachmentSnapshot(
                a.Id,
                !string.IsNullOrWhiteSpace(a.SavedFileName) ? a.SavedFileName! : a.OriginalFileName!,
                a.AccItemId))
            .Where(a => !string.IsNullOrWhiteSpace(a.FileName))
            .ToListAsync(cancellationToken);
    }

    private static async Task<string?> ResolveActingUserLoginAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int operatorUserId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var login = await db.Siusers.AsNoTracking()
            .Where(u => u.Id == operatorUserId)
            .Select(u => u.LoginName)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(login))
        {
            evidence.Fail(
                "cert.prp.source_ingest",
                $"Operator SIUser id={operatorUserId} has no LoginName for IngestToInboxAsync.");
            return null;
        }

        return login.Trim();
    }

    private static async Task<int?> FindInboxMessageIdAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        EmailMessageDetails details,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(details.InternetMessageId))
        {
            var unique = EmailMessageIdentity.GetMessageUniqueId(
                details.InternetMessageId,
                details.MessageId);
            var byUnique = await db.EmailInboxMessages
                .AsNoTracking()
                .Where(m => m.MessageUniqueId == unique || m.InternetMessageId == details.InternetMessageId)
                .OrderByDescending(m => m.Id)
                .Select(m => (int?)m.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byUnique is > 0)
            {
                return byUnique;
            }
        }

        var gmailKey = $"gmail:{details.MessageId}";
        return await db.EmailInboxMessages
            .AsNoTracking()
            .Where(m => m.MessageUniqueId == details.MessageId || m.MessageUniqueId == gmailKey)
            .OrderByDescending(m => m.Id)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<bool> HasActiveProposalForInboxAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int proposalDefinitionId,
        int inboxMessageId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.WorkflowInstances.AsNoTracking().AnyAsync(
            w => w.WorkflowDefinitionId == proposalDefinitionId
                 && w.TriggerType == WorkflowTriggerType.Email
                 && w.TriggerEntityId == inboxMessageId
                 && w.Status != WorkflowStatus.Completed
                 && w.Status != WorkflowStatus.Cancelled,
            cancellationToken);
    }

    private static string TrimSubject(string subject) =>
        subject.Length <= 80 ? subject : subject[..77] + "...";
}
