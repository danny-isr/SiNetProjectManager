using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Proves <see cref="TaskTypeCodes.FileQuoteMaterial"/> through production Email/ACC filing/move services,
/// independent ACC read-back, optional Gmail filing read-back, and only then
/// <see cref="ReviewCompletionEvents.ReviewMaterialFiled"/>.
/// </summary>
internal static class SystemCertificationPrpFileMaterialProof
{
    internal sealed record ExpectedAttachment(
        int InboxAttachmentId,
        string FileName);

    internal sealed record PreMoveExpectedState(
        string AccProjectId,
        string AccTargetFolderId,
        int CertProjectId,
        IReadOnlyList<ExpectedAttachment> Attachments,
        IReadOnlyDictionary<string, AccFolderBrowseEntry> PreWriteItemsByFileName);

    internal sealed record PostMoveAccIds(
        int InboxAttachmentId,
        string? AccItemId,
        string? AccVersionId);

    /// <summary>
    /// Runs acc.write → acc.readback → gmail filing readback/N/A → transition.FileQuoteMaterial.
    /// Returns false when the task must stay open and the corridor must stop.
    /// </summary>
    internal static async Task<bool> TryProveAndCompleteAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationHost.SystemCertificationRunContext context,
        SystemCertificationEvidence evidence,
        int fileQuoteTaskId,
        int certProjectId,
        int instanceId,
        int operatorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(integrity);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evidence);

        if (!context.Acc.IsEnabled || context.Acc.Violation is not null)
        {
            evidence.Fail(
                "cert.prp.acc.write",
                "FileQuoteMaterial requires a valid ACC layer; direct ReviewMaterialFiled is forbidden.");
            return false;
        }

        var inboxMessageId = await ResolveLinkedInboxMessageIdAsync(
            dbFactory, fileQuoteTaskId, cancellationToken);
        if (inboxMessageId is not int inboxId)
        {
            evidence.Fail(
                "cert.prp.acc.write",
                $"FileQuoteMaterial task {fileQuoteTaskId} has no EmailInboxMessage work-target link.");
            return false;
        }

        var accProjectId = await EnsureProjectMappingAsync(
            provider, dbFactory, context, certProjectId, evidence, cancellationToken);
        if (accProjectId is null)
        {
            return false;
        }

        if (!await TagInboxAttachmentsAsync(
                provider, dbFactory, inboxId, certProjectId, operatorUserId, evidence, cancellationToken))
        {
            return false;
        }

        var preMoveExpected = await CapturePreMoveExpectedStateAsync(
            provider,
            dbFactory,
            accProjectId,
            certProjectId,
            inboxId,
            evidence,
            cancellationToken);
        if (preMoveExpected is null)
        {
            return false;
        }

        var move = provider.GetRequiredService<IEmailMoveToProjectService>();
        if (!move.IsAvailable)
        {
            evidence.Fail("cert.prp.acc.write", "IEmailMoveToProjectService reports IsAvailable=false.");
            return false;
        }

        var writeResult = await move.MoveAsync(
            new EmailMoveToProjectDetailCommand(
                inboxId,
                certProjectId,
                TaskId: null,
                TaskResultCode: null),
            cancellationToken);

        if (!writeResult.Succeeded)
        {
            evidence.Fail(
                "cert.prp.acc.write",
                $"IEmailMoveToProjectService.MoveAsync failed: {writeResult.Message}. "
                + "Task remains open; workflow must not advance.");
            return false;
        }

        evidence.Pass(
            "cert.prp.acc.write",
            $"Production move via IEmailMoveToProjectService → NativeEmailMoveToProjectExecutor: "
            + $"moved={writeResult.MovedCount} alreadySameSource={writeResult.AlreadySameSourceCount} "
            + $"total={writeResult.TotalCount} failed={writeResult.FailedCount}. "
            + $"Immutable pre-move expected count={preMoveExpected.Attachments.Count} "
            + $"for project {preMoveExpected.CertProjectId} / ACC folder '{preMoveExpected.AccTargetFolderId}'.");

        var postMoveAccIds = await LoadPostMoveAccIdsAsync(
            dbFactory,
            preMoveExpected.Attachments,
            cancellationToken);

        if (!await VerifyAccReadBackAsync(
                provider,
                preMoveExpected,
                postMoveAccIds,
                evidence,
                cancellationToken))
        {
            return false;
        }

        if (!await VerifyGmailFilingReadBackAsync(provider, evidence, cancellationToken))
        {
            return false;
        }

        var completion = provider.GetRequiredService<ITaskCompletionService>();
        var taskLinkIds = await ResolveCompletedTaskLinkIdsAsync(
            dbFactory, fileQuoteTaskId, inboxId, cancellationToken);
        if (taskLinkIds.Count == 0)
        {
            evidence.Fail(
                "cert.prp.transition.FileQuoteMaterial",
                $"No EmailInboxMessage work-target TaskLink found for inbox {inboxId}.");
            return false;
        }

        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(
                fileQuoteTaskId,
                ReviewCompletionEvents.ReviewMaterialFiled,
                TaskResultCode: null,
                taskLinkIds,
                operatorUserId),
            cancellationToken);

        if (!outcome.Success)
        {
            evidence.Fail(
                "cert.prp.transition.FileQuoteMaterial",
                $"ReviewMaterialFiled refused after ACC read-back: {Trim(outcome.ErrorMessage)}.");
            return false;
        }

        await SystemCertificationTransitionAssertions.AssertAfterTransitionAsync(
            dbFactory,
            integrity,
            evidence,
            "cert.prp.transition.FileQuoteMaterial",
            instanceId,
            fileQuoteTaskId,
            ProposalStageCodes.MaterialCheck,
            cancellationToken);

        return true;
    }

    private static async Task<int?> ResolveLinkedInboxMessageIdAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int taskId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TaskLinks.AsNoTracking()
            .Where(l => l.TaskId == taskId
                        && l.IsWorkTarget
                        && l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage)
            .OrderBy(l => l.Id)
            .Select(l => (int?)l.LinkedEntityId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<string?> EnsureProjectMappingAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationHost.SystemCertificationRunContext context,
        int projectId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        var provisioner = provider.GetRequiredService<IProjectAccMappingProvisioner>();
        await provisioner.EnsureMappingAsync(projectId, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var mapping = await db.ProjectAccMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId, cancellationToken);

        if (mapping is null || string.IsNullOrWhiteSpace(mapping.AccProjectId))
        {
            evidence.Fail(
                "cert.prp.acc.write",
                $"Project {projectId} has no ACC mapping after IProjectAccMappingProvisioner.EnsureMappingAsync.");
            return null;
        }

        context.AccGuard?.Allow(
            mapping.AccProjectId,
            $"[SYS-CERT] project {projectId} ACC mapping before FileQuoteMaterial move");

        return mapping.AccProjectId;
    }

    private static async Task<bool> TagInboxAttachmentsAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int inboxMessageId,
        int projectId,
        int operatorUserId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        var tagging = provider.GetRequiredService<IEmailAttachmentTaggingService>();
        var attachments = await tagging.LoadInboxAttachmentsAsync(inboxMessageId, cancellationToken);
        var taggable = attachments.Where(a => a.IsTaggable).ToList();
        if (taggable.Count == 0)
        {
            evidence.Fail(
                "cert.prp.acc.write",
                "No taggable inbox attachments exist for MoveToProject through the production tagging service.");
            return false;
        }

        var targets = await tagging.LoadTagTargetsAsync(projectId, cancellationToken);
        var target = targets.FirstOrDefault();
        if (target is null)
        {
            evidence.Fail(
                "cert.prp.acc.write",
                "No OutSidData catalog slot exists — cannot tag attachments for MoveToProject.");
            return false;
        }

        var alternatives = await tagging.LoadAlternativesAsync(projectId, cancellationToken);
        var alternativeId = EmailProjectAlternativeOption.ResolveDefaultId(alternatives);

        foreach (var attachment in taggable)
        {
            var result = await tagging.SetTagAsync(
                new EmailAttachmentTagCommand(
                    attachment.InboxAttachmentId,
                    target.ProjectFileId,
                    alternativeId,
                    operatorUserId),
                cancellationToken);

            if (!result.Succeeded)
            {
                evidence.Fail(
                    "cert.prp.acc.write",
                    $"IEmailAttachmentTaggingService.SetTagAsync failed for attachment "
                    + $"{attachment.InboxAttachmentId}: {result.ErrorMessage}");
                return false;
            }
        }

        return true;
    }

    private static async Task<PreMoveExpectedState?> CapturePreMoveExpectedStateAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        string accProjectId,
        int certProjectId,
        int inboxMessageId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var mapping = await db.ProjectAccMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == certProjectId, cancellationToken);

        if (mapping is null || string.IsNullOrWhiteSpace(mapping.AccTargetFolderId))
        {
            evidence.Fail(
                "cert.prp.acc.write",
                $"Project {certProjectId} mapping has no AccTargetFolderId before MoveAsync.");
            return null;
        }

        var rows = await db.EmailInboxAttachments.AsNoTracking()
            .Where(a => a.MessageId == inboxMessageId && a.ProjectFileId != null)
            .Select(a => new
            {
                a.Id,
                a.SavedFileName,
                a.OriginalFileName,
            })
            .ToListAsync(cancellationToken);

        var attachments = new List<ExpectedAttachment>(rows.Count);
        foreach (var row in rows)
        {
            var fileName = !string.IsNullOrWhiteSpace(row.SavedFileName)
                ? row.SavedFileName.Trim()
                : row.OriginalFileName?.Trim();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            attachments.Add(new ExpectedAttachment(row.Id, fileName));
        }

        if (attachments.Count == 0)
        {
            evidence.Fail(
                "cert.prp.acc.write",
                "No tagged inbox attachments with filenames exist before MoveAsync — cannot define expected ACC set.");
            return null;
        }

        var browser = provider.GetRequiredService<IAccFolderBrowserService>();
        var browse = await browser.BrowseAsync(accProjectId, mapping.AccTargetFolderId, cancellationToken);
        if (browse is null)
        {
            evidence.Fail(
                "cert.prp.acc.write",
                $"Pre-write IAccFolderBrowserService.BrowseAsync returned null for project '{accProjectId}' "
                + $"folder '{mapping.AccTargetFolderId}'.");
            return null;
        }

        var preWriteItems = browse.Entries
            .Where(e => e.Kind == AccFolderEntryKind.Item)
            .GroupBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        evidence.Pass(
            "cert.prp.acc.write",
            $"Captured immutable pre-move expected set: {attachments.Count} attachment id(s), "
            + $"{preWriteItems.Count} pre-existing ACC item(s) in folder '{mapping.AccTargetFolderId}'.");

        return new PreMoveExpectedState(
            accProjectId,
            mapping.AccTargetFolderId,
            certProjectId,
            attachments,
            preWriteItems);
    }

    private static async Task<IReadOnlyList<PostMoveAccIds>> LoadPostMoveAccIdsAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IReadOnlyList<ExpectedAttachment> expectedAttachments,
        CancellationToken cancellationToken)
    {
        var ids = expectedAttachments.Select(a => a.InboxAttachmentId).ToList();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.EmailInboxAttachments.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new PostMoveAccIds(a.Id, a.AccItemId, a.AccVersionId))
            .ToListAsync(cancellationToken);
    }

    private static async Task<bool> VerifyAccReadBackAsync(
        IServiceProvider provider,
        PreMoveExpectedState expected,
        IReadOnlyList<PostMoveAccIds> postMoveAccIds,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        var browser = provider.GetRequiredService<IAccFolderBrowserService>();
        var browse = await browser.BrowseAsync(
            expected.AccProjectId,
            expected.AccTargetFolderId,
            cancellationToken);
        if (browse is null)
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                $"IAccFolderBrowserService.BrowseAsync returned null for project '{expected.AccProjectId}' "
                + $"folder '{expected.AccTargetFolderId}'.");
            return false;
        }

        if (!string.Equals(browse.ProjectId, expected.AccProjectId, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                $"ACC browse project '{browse.ProjectId}' != expected '{expected.AccProjectId}'.");
            return false;
        }

        if (!string.Equals(browse.FolderId, expected.AccTargetFolderId, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                $"ACC browse folder '{browse.FolderId}' != expected target '{expected.AccTargetFolderId}'.");
            return false;
        }

        var items = browse.Entries
            .Where(e => e.Kind == AccFolderEntryKind.Item)
            .ToList();

        var postMoveByAttachmentId = postMoveAccIds.ToDictionary(a => a.InboxAttachmentId);
        var failures = new List<string>();
        foreach (var attachment in expected.Attachments)
        {
            var matches = items.Where(i =>
                    string.Equals(i.DisplayName, attachment.FileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                failures.Add($"missing '{attachment.FileName}' (attachment id={attachment.InboxAttachmentId})");
                continue;
            }

            var match = matches[0];
            if (match.FileSize <= 0)
            {
                failures.Add($"item '{match.DisplayName}' has no file size metadata");
            }

            if (expected.PreWriteItemsByFileName.TryGetValue(attachment.FileName, out var preWrite))
            {
                if (string.Equals(match.Id, preWrite.Id, StringComparison.OrdinalIgnoreCase)
                    && !ItemLooksUpdated(preWrite, match))
                {
                    failures.Add(
                        $"item '{attachment.FileName}' unchanged since pre-write snapshot "
                        + $"(id='{match.Id}') — stale file would cause false PASS");
                }
            }

            if (postMoveByAttachmentId.TryGetValue(attachment.InboxAttachmentId, out var written)
                && !string.IsNullOrWhiteSpace(written.AccItemId)
                && !string.Equals(match.Id, written.AccItemId, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"filename '{attachment.FileName}' browse item '{match.Id}' != post-write AccItemId "
                    + $"'{written.AccItemId}'");
            }
        }

        if (items.Count < expected.Attachments.Count)
        {
            failures.Add(
                $"folder item count {items.Count} < immutable pre-move expected count {expected.Attachments.Count}");
        }

        if (failures.Count > 0)
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                "Independent IAccFolderBrowserService read-back failed against pre-move expected set: "
                + string.Join("; ", failures));
            return false;
        }

        evidence.Pass(
            "cert.prp.acc.readback",
            $"Independent IAccFolderBrowserService read-back matched immutable pre-move expected set "
            + $"({expected.Attachments.Count} attachment id(s)/filename(s)) in project '{expected.AccProjectId}', "
            + $"folder '{expected.AccTargetFolderId}', with new-or-updated item proof.");
        return true;
    }

    private static bool ItemLooksUpdated(AccFolderBrowseEntry before, AccFolderBrowseEntry after)
    {
        if (!string.Equals(before.Id, after.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (after.LastModifiedTime is DateTime afterModified
            && before.LastModifiedTime is DateTime beforeModified
            && afterModified > beforeModified)
        {
            return true;
        }

        if (after.CreateTime is DateTime afterCreated
            && before.CreateTime is DateTime beforeCreated
            && afterCreated > beforeCreated)
        {
            return true;
        }

        return after.FileSize != before.FileSize;
    }

    private static Task<bool> VerifyGmailFilingReadBackAsync(
        IServiceProvider provider,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        _ = provider;
        _ = cancellationToken;

        evidence.NotApplicable(
            "cert.prp.gmail.filing.readback",
            "IEmailMoveToProjectService / NativeEmailMoveToProjectExecutor moves tagged attachments to the "
            + "project ACC folder only; it does not call IEmailFilingService or mutate Gmail labels.");
        return Task.FromResult(true);
    }

    private static async Task<List<int>> ResolveCompletedTaskLinkIdsAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int taskId,
        int inboxMessageId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.TaskLinks.AsNoTracking()
            .Where(l => l.TaskId == taskId
                        && l.IsWorkTarget
                        && l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage
                        && l.LinkedEntityId == inboxMessageId)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    private static string Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 240 ? oneLine : oneLine[..237] + "...";
    }
}
