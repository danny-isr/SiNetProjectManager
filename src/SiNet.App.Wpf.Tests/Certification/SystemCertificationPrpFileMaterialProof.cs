using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Autodesk.Metadata;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Email.Acc;
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
    internal sealed record FilingEvidenceSteps(
        string AccWrite,
        string AccReadback,
        string Transition,
        string GmailFilingReadback)
    {
        public static FilingEvidenceSteps Prp { get; } = new(
            "cert.prp.acc.write",
            "cert.prp.acc.readback",
            "cert.prp.transition.FileQuoteMaterial",
            "cert.prp.gmail.filing.readback");

        public static FilingEvidenceSteps Opn { get; } = new(
            "cert.opn.acc.write",
            "cert.opn.acc.readback",
            "cert.opn.transition.FileInitialMaterials",
            "cert.opn.gmail.filing.readback");
    }

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
    /// Runs acc.write → acc.readback → gmail filing readback/N/A → filing-task transition.
    /// Returns false when the task must stay open and the corridor must stop.
    /// </summary>
    internal static async Task<bool> TryProveAndCompleteAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationHost.SystemCertificationRunContext context,
        SystemCertificationEvidence evidence,
        int filingTaskId,
        int certProjectId,
        int instanceId,
        int operatorUserId,
        CancellationToken cancellationToken = default,
        FilingEvidenceSteps? steps = null,
        string? expectedStageAfter = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(integrity);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evidence);

        steps ??= FilingEvidenceSteps.Prp;
        expectedStageAfter ??= ProposalStageCodes.MaterialCheck;

        if (!context.Acc.IsEnabled || context.Acc.Violation is not null)
        {
            evidence.Fail(
                steps.AccWrite,
                "Filing task requires a valid ACC layer; direct ReviewMaterialFiled is forbidden.");
            return false;
        }

        var inboxMessageId = await ResolveLinkedInboxMessageIdAsync(
            dbFactory, filingTaskId, cancellationToken);
        if (inboxMessageId is not int inboxId)
        {
            evidence.Fail(
                steps.AccWrite,
                $"Filing task {filingTaskId} has no EmailInboxMessage work-target link.");
            return false;
        }

        var accProjectId = await EnsureProjectMappingAsync(
            provider, dbFactory, context, certProjectId, evidence, cancellationToken, steps);
        if (accProjectId is null)
        {
            return false;
        }

        if (!await TagInboxAttachmentsAsync(
                provider, dbFactory, inboxId, certProjectId, operatorUserId, evidence, cancellationToken, steps))
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
            cancellationToken,
            steps);
        if (preMoveExpected is null)
        {
            return false;
        }

        var move = provider.GetRequiredService<IEmailMoveToProjectService>();
        if (!move.IsAvailable)
        {
            evidence.Fail(steps.AccWrite, "IEmailMoveToProjectService reports IsAvailable=false.");
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
                steps.AccWrite,
                $"IEmailMoveToProjectService.MoveAsync failed: {writeResult.Message}. "
                + "Task remains open; workflow must not advance.");
            return false;
        }

        evidence.Pass(
            steps.AccWrite,
            $"Production move via IEmailMoveToProjectService → NativeEmailMoveToProjectExecutor: "
            + $"moved={writeResult.MovedCount} alreadySameSource={writeResult.AlreadySameSourceCount} "
            + $"total={writeResult.TotalCount} failed={writeResult.FailedCount}. "
            + $"Immutable pre-move expected count={preMoveExpected.Attachments.Count} "
            + $"for project {preMoveExpected.CertProjectId} / ACC folder '{preMoveExpected.AccTargetFolderId}'.");

        if (!await VerifyAccReadBackAsync(
                provider,
                dbFactory,
                inboxId,
                preMoveExpected,
                evidence,
                cancellationToken,
                steps))
        {
            return false;
        }

        if (!await VerifyGmailFilingReadBackAsync(provider, evidence, cancellationToken, steps))
        {
            return false;
        }

        var completion = provider.GetRequiredService<ITaskCompletionService>();
        var taskLinkIds = await ResolveCompletedTaskLinkIdsAsync(
            dbFactory, filingTaskId, inboxId, cancellationToken);
        if (taskLinkIds.Count == 0)
        {
            evidence.Fail(
                steps.Transition,
                $"No EmailInboxMessage work-target TaskLink found for inbox {inboxId}.");
            return false;
        }

        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(
                filingTaskId,
                ReviewCompletionEvents.ReviewMaterialFiled,
                TaskResultCode: null,
                taskLinkIds,
                operatorUserId),
            cancellationToken);

        if (!outcome.Success)
        {
            evidence.Fail(
                steps.Transition,
                $"ReviewMaterialFiled refused after ACC read-back: {Trim(outcome.ErrorMessage)}.");
            return false;
        }

        if (!await SystemCertificationTransitionAssertions.AssertAfterTransitionAsync(
                dbFactory,
                integrity,
                evidence,
                steps.Transition,
                instanceId,
                filingTaskId,
                expectedStageAfter,
                cancellationToken))
        {
            return false;
        }

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
        CancellationToken cancellationToken,
        FilingEvidenceSteps steps)
    {
        var provisioner = provider.GetRequiredService<IProjectAccMappingProvisioner>();
        await provisioner.EnsureMappingAsync(projectId, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var mapping = await db.ProjectAccMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId, cancellationToken);

        if (mapping is null || string.IsNullOrWhiteSpace(mapping.AccProjectId))
        {
            evidence.Fail(
                steps.AccWrite,
                $"Project {projectId} has no ACC mapping after IProjectAccMappingProvisioner.EnsureMappingAsync.");
            return null;
        }

        context.AccGuard?.Allow(
            mapping.AccProjectId,
            $"[SYS-CERT] project {projectId} ACC mapping before filing move");

        return mapping.AccProjectId;
    }

    private static async Task<bool> TagInboxAttachmentsAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int inboxMessageId,
        int projectId,
        int operatorUserId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken,
        FilingEvidenceSteps steps)
    {
        var tagging = provider.GetRequiredService<IEmailAttachmentTaggingService>();
        var attachments = await tagging.LoadInboxAttachmentsAsync(inboxMessageId, cancellationToken);
        var taggable = attachments.Where(a => a.IsTaggable).ToList();
        if (taggable.Count == 0)
        {
            evidence.Fail(
                steps.AccWrite,
                "No taggable inbox attachments exist for MoveToProject through the production tagging service.");
            return false;
        }

        var targets = await tagging.LoadTagTargetsAsync(projectId, cancellationToken);
        if (targets.Count == 0)
        {
            evidence.Fail(
                steps.AccWrite,
                "No OutSidData catalog slot exists — cannot tag attachments for MoveToProject.");
            return false;
        }

        var accTarget = await ResolveAccOutSidDataTargetAsync(dbFactory, targets, evidence, cancellationToken, steps);
        if (accTarget is null)
        {
            return false;
        }

        var alternatives = await tagging.LoadAlternativesAsync(projectId, cancellationToken);
        var alternativeId = EmailProjectAlternativeOption.ResolveDefaultId(alternatives);

        foreach (var attachment in taggable)
        {
            var result = await tagging.SetTagAsync(
                new EmailAttachmentTagCommand(
                    attachment.InboxAttachmentId,
                    accTarget.ProjectFileId,
                    alternativeId,
                    operatorUserId),
                cancellationToken);

            if (!result.Succeeded)
            {
                evidence.Fail(
                    steps.AccWrite,
                    $"IEmailAttachmentTaggingService.SetTagAsync failed for attachment "
                    + $"{attachment.InboxAttachmentId}: {result.ErrorMessage}");
                return false;
            }
        }

        return true;
    }

    private static async Task<EmailAttachmentTagTarget?> ResolveAccOutSidDataTargetAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IReadOnlyList<EmailAttachmentTagTarget> targets,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken,
        FilingEvidenceSteps steps)
    {
        var targetIds = targets.Select(t => t.ProjectFileId).ToList();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var accFileIds = await db.ProjectFiles.AsNoTracking()
            .Where(pf => targetIds.Contains(pf.Id)
                         && pf.OutSidData == true
                         && pf.StorageDestination == FileStorageDestination.Acc)
            .OrderBy(pf => pf.Title)
            .ThenBy(pf => pf.Number)
            .Select(pf => pf.Id)
            .ToListAsync(cancellationToken);

        if (accFileIds.Count == 0)
        {
            evidence.Fail(
                steps.AccWrite,
                "No OutSidData ProjectFile with StorageDestination=Acc exists for ACC filing proof "
                + $"(catalog targets={targets.Count}). Tagging a FileServer slot would not prove ACC write.");
            return null;
        }

        var preferredId = accFileIds[0];
        return targets.First(t => t.ProjectFileId == preferredId);
    }

    private static async Task<PreMoveExpectedState?> CapturePreMoveExpectedStateAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        string accProjectId,
        int certProjectId,
        int inboxMessageId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken,
        FilingEvidenceSteps steps)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var mapping = await db.ProjectAccMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == certProjectId, cancellationToken);

        if (mapping is null || string.IsNullOrWhiteSpace(mapping.AccTargetFolderId))
        {
            evidence.Fail(
                steps.AccWrite,
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
                steps.AccWrite,
                "No tagged inbox attachments with filenames exist before MoveAsync — cannot define expected ACC set.");
            return null;
        }

        var browser = provider.GetRequiredService<IAccFolderBrowserService>();
        var browse = await browser.BrowseAsync(accProjectId, mapping.AccTargetFolderId, cancellationToken);
        if (browse is null)
        {
            evidence.Fail(
                steps.AccWrite,
                $"Pre-write IAccFolderBrowserService.BrowseAsync returned null for project '{accProjectId}' "
                + $"folder '{mapping.AccTargetFolderId}'.");
            return null;
        }

        var preWriteItems = browse.Entries
            .Where(e => e.Kind == AccFolderEntryKind.Item)
            .GroupBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        evidence.Pass(
            steps.AccWrite,
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
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int inboxMessageId,
        PreMoveExpectedState expected,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken,
        FilingEvidenceSteps steps)
    {
        var metadata = provider.GetRequiredService<IAccItemMetadataService>();
        var browser = provider.GetRequiredService<IAccFolderBrowserService>();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var message = await db.EmailInboxMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == inboxMessageId, cancellationToken);
        if (message is null
            || string.IsNullOrWhiteSpace(message.InboxAccProjectId))
        {
            evidence.Fail(
                steps.AccReadback,
                $"Inbox id={inboxMessageId} missing InboxAccProjectId for post-move ACC metadata read.");
            return false;
        }

        var attachmentRows = await db.EmailInboxAttachments.AsNoTracking()
            .Where(a => expected.Attachments.Select(x => x.InboxAttachmentId).Contains(a.Id))
            .Select(a => new { a.Id, a.AccItemId, a.SavedFileName, a.OriginalFileName })
            .ToListAsync(cancellationToken);

        var failures = new List<string>();
        var matchedCount = 0;
        foreach (var attachment in expected.Attachments)
        {
            var row = attachmentRows.FirstOrDefault(a => a.Id == attachment.InboxAttachmentId);
            if (row is null || string.IsNullOrWhiteSpace(row.AccItemId))
            {
                failures.Add($"attachment id={attachment.InboxAttachmentId} has no inbox AccItemId for metadata read");
                continue;
            }

            var meta = await metadata.ReadAttributesAsync(
                message.InboxAccProjectId,
                row.AccItemId,
                row.SavedFileName ?? row.OriginalFileName,
                cancellationToken);
            if (!meta.Success)
            {
                failures.Add(
                    $"metadata read failed for attachment id={attachment.InboxAttachmentId}: {meta.ErrorMessage}");
                continue;
            }

            if (!IsTruthy(meta.Attributes, SidecarMetadata.InboxAccAttributeNames.MoveMovedToProject))
            {
                failures.Add($"attachment id={attachment.InboxAttachmentId} missing MoveMovedToProject=true after MoveAsync");
                continue;
            }

            if (!string.Equals(
                    meta.Attributes.GetValueOrDefault(SidecarMetadata.InboxAccAttributeNames.MoveTargetDestination),
                    nameof(FileStorageDestination.Acc),
                    StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"attachment id={attachment.InboxAttachmentId} TargetDestination="
                    + $"'{meta.Attributes.GetValueOrDefault(SidecarMetadata.InboxAccAttributeNames.MoveTargetDestination) ?? "<null>"}' "
                    + "(expected Acc for cert.prp.acc.readback)");
                continue;
            }

            var targetFolderId = meta.Attributes.GetValueOrDefault(SidecarMetadata.InboxAccAttributeNames.MoveTargetAccFolderId);
            var targetFileName = meta.Attributes.GetValueOrDefault(SidecarMetadata.InboxAccAttributeNames.MoveTargetFileName);
            var targetItemId = meta.Attributes.GetValueOrDefault(SidecarMetadata.InboxAccAttributeNames.MoveTargetAccItemId);
            if (string.IsNullOrWhiteSpace(targetFolderId) || string.IsNullOrWhiteSpace(targetFileName))
            {
                failures.Add(
                    $"attachment id={attachment.InboxAttachmentId} missing TargetAccFolderId/TargetFileName "
                    + "on ACC move metadata");
                continue;
            }

            var browse = await browser.BrowseAsync(
                expected.AccProjectId,
                targetFolderId,
                cancellationToken);
            if (browse is null)
            {
                failures.Add(
                    $"BrowseAsync null for ACC folder '{targetFolderId}' "
                    + $"(attachment id={attachment.InboxAttachmentId})");
                continue;
            }

            if (!SystemCertificationAccIdentity.ProjectIdsMatch(browse.ProjectId, expected.AccProjectId))
            {
                failures.Add(
                    $"browse project '{browse.ProjectId}' != expected '{expected.AccProjectId}' "
                    + $"(attachment id={attachment.InboxAttachmentId})");
                continue;
            }

            var matches = browse.Entries
                .Where(e => e.Kind == AccFolderEntryKind.Item
                            && string.Equals(e.DisplayName, targetFileName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
            {
                failures.Add(
                    $"missing filed ACC item '{targetFileName}' in folder '{targetFolderId}' "
                    + $"(attachment id={attachment.InboxAttachmentId})");
                continue;
            }

            var match = matches[0];
            if (!string.IsNullOrWhiteSpace(targetItemId)
                && !string.Equals(match.Id, targetItemId, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"browse item '{match.Id}' != MoveTargetAccItemId '{targetItemId}' "
                    + $"(attachment id={attachment.InboxAttachmentId})");
                continue;
            }

            matchedCount++;
        }

        if (failures.Count > 0 || matchedCount < expected.Attachments.Count)
        {
            evidence.Fail(
                steps.AccReadback,
                "Independent ACC read-back against post-move Move metadata failed: "
                + string.Join("; ", failures));
            return false;
        }

        evidence.Pass(
            steps.AccReadback,
            $"Independent ACC browse matched Move metadata for {matchedCount} ACC-filed attachment(s) "
            + $"in project '{expected.AccProjectId}'.");
        return true;
    }

    private static bool IsTruthy(IReadOnlyDictionary<string, string?> attributes, string key) =>
        attributes.TryGetValue(key, out var value)
        && (value == "1"
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

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
        CancellationToken cancellationToken,
        FilingEvidenceSteps steps)
    {
        _ = provider;
        _ = cancellationToken;

        evidence.NotApplicable(
            steps.GmailFilingReadback,
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
