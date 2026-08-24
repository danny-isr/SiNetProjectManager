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
    internal sealed record ExpectedMovedFile(
        string FileName,
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
            + "Succeeded/AllFilesTransferred are recorded but are not treated as external proof.");

        var expectedFiles = await LoadExpectedMovedFilesAsync(dbFactory, inboxId, cancellationToken);
        if (expectedFiles.Count == 0)
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                "No tagged inbox attachments with filenames remain after the ACC write.");
            return false;
        }

        if (!await VerifyAccReadBackAsync(
                provider,
                dbFactory,
                accProjectId,
                certProjectId,
                expectedFiles,
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

    private static async Task<List<ExpectedMovedFile>> LoadExpectedMovedFilesAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int inboxMessageId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.EmailInboxAttachments.AsNoTracking()
            .Where(a => a.MessageId == inboxMessageId && a.ProjectFileId != null)
            .Select(a => new ExpectedMovedFile(
                !string.IsNullOrWhiteSpace(a.SavedFileName) ? a.SavedFileName! : a.OriginalFileName!,
                a.AccItemId,
                a.AccVersionId))
            .Where(a => !string.IsNullOrWhiteSpace(a.FileName))
            .ToListAsync(cancellationToken);
    }

    private static async Task<bool> VerifyAccReadBackAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        string accProjectId,
        int certProjectId,
        IReadOnlyList<ExpectedMovedFile> expectedFiles,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var mapping = await db.ProjectAccMappings.AsNoTracking()
            .FirstAsync(m => m.ProjectId == certProjectId, cancellationToken);

        if (!string.Equals(mapping.AccProjectId, accProjectId, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                $"ACC mapping project id '{mapping.AccProjectId}' != write target '{accProjectId}'.");
            return false;
        }

        var targetFolderId = mapping.AccTargetFolderId;
        if (string.IsNullOrWhiteSpace(targetFolderId))
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                $"Project {certProjectId} mapping has no AccTargetFolderId for independent read-back.");
            return false;
        }

        var browser = provider.GetRequiredService<IAccFolderBrowserService>();
        var browse = await browser.BrowseAsync(accProjectId, targetFolderId, cancellationToken);
        if (browse is null)
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                $"IAccFolderBrowserService.BrowseAsync returned null for project '{accProjectId}' "
                + $"folder '{targetFolderId}'.");
            return false;
        }

        if (!string.Equals(browse.ProjectId, accProjectId, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                $"ACC browse project '{browse.ProjectId}' != expected '{accProjectId}'.");
            return false;
        }

        if (!string.Equals(browse.FolderId, targetFolderId, StringComparison.OrdinalIgnoreCase))
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                $"ACC browse folder '{browse.FolderId}' != expected target '{targetFolderId}'.");
            return false;
        }

        var items = browse.Entries
            .Where(e => e.Kind == AccFolderEntryKind.Item)
            .ToList();

        var failures = new List<string>();
        foreach (var expected in expectedFiles)
        {
            var matches = items.Where(i =>
                    string.Equals(i.DisplayName, expected.FileName, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(expected.AccItemId)
                        && string.Equals(i.Id, expected.AccItemId, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matches.Count == 0)
            {
                failures.Add($"missing '{expected.FileName}'");
                continue;
            }

            var match = matches[0];
            if (!string.IsNullOrWhiteSpace(expected.AccItemId)
                && !string.Equals(match.Id, expected.AccItemId, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"filename '{expected.FileName}' found as item '{match.Id}', expected item "
                    + $"'{expected.AccItemId}'");
            }

            if (match.FileSize <= 0)
            {
                failures.Add($"item '{match.DisplayName}' has no file size metadata");
            }
        }

        if (items.Count < expectedFiles.Count)
        {
            failures.Add(
                $"folder item count {items.Count} < expected moved file count {expectedFiles.Count}");
        }

        if (failures.Count > 0)
        {
            evidence.Fail(
                "cert.prp.acc.readback",
                "Independent IAccFolderBrowserService read-back failed: "
                + string.Join("; ", failures));
            return false;
        }

        evidence.Pass(
            "cert.prp.acc.readback",
            $"Independent IAccFolderBrowserService read-back matched project '{accProjectId}', "
            + $"folder '{targetFolderId}', {expectedFiles.Count} expected filename(s), item ids and sizes.");
        return true;
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
