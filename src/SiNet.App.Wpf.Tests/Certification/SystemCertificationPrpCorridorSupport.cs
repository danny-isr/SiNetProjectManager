using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Email;
using SiNet.Application.Email.Detail;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// PRP corridor for the certification tier. Starts only through
/// <see cref="IEmailSuggestedActionExecutionService"/> / CreatePriceQuote and completes tasks through
/// production seams with transition assertions after every step.
/// </summary>
internal static class SystemCertificationPrpCorridorSupport
{
    internal sealed record CorridorInbox(
        int? InboxMessageId,
        EmailGmailSourceIdentity? GmailSource,
        string SelectionDetail);

    internal sealed record Preconditions(
        int ProposalDefinitionId,
        int PlaceId,
        int CompanyId,
        int ContactId,
        int PlanningJobTypeId);

    private static readonly Dictionary<string, string> DesiredResultByTaskType = new(StringComparer.Ordinal)
    {
        [TaskTypeCodes.IdentifyQuoteRequest] = TaskResultCodes.QuoteRequestDetected,
        [TaskTypeCodes.OpenQuoteProject] = TaskResultCodes.ProjectOpened,
        [TaskTypeCodes.CheckQuoteMaterialCompleteness] = TaskResultCodes.MaterialComplete,
        [TaskTypeCodes.PrepareQuoteCalculation] = TaskResultCodes.QuoteCalculationCompleted,
        [TaskTypeCodes.PrepareQuoteDocument] = TaskResultCodes.QuotePrepared,
        [TaskTypeCodes.ApproveQuoteInternal] = TaskResultCodes.QuoteApprovedInternally,
    };

    internal static async Task<Preconditions?> TryResolvePreconditionsAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var proposal = await db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == WorkflowCodes.Proposal && d.IsActive, cancellationToken);
        if (proposal is null)
        {
            evidence.Fail("cert.prp.preconditions", "Active Proposal workflow definition not found.");
            return null;
        }

        var planningDefinitionId = await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.Code == WorkflowCodes.PlanningWorkflow && d.IsActive)
            .Select(d => d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (planningDefinitionId == 0)
        {
            evidence.Fail(
                "cert.prp.preconditions",
                $"Active {WorkflowCodes.PlanningWorkflow} definition not found.");
            return null;
        }

        var planningJobTypeId = await db.ProjectTypeWorkflowDefinitions
            .AsNoTracking()
            .Where(m => m.WorkflowDefinitionId == planningDefinitionId && m.IsEnabled)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.ProjectTypeId)
            .FirstOrDefaultAsync(cancellationToken);

        var place = await db.Places.AsNoTracking().FirstOrDefaultAsync(
            p => p.Title == SystemCertificationEnvironment.RequiredAccPlaceTitle,
            cancellationToken);
        if (place is null)
        {
            evidence.Fail(
                "cert.prp.preconditions",
                $"Place '{SystemCertificationEnvironment.RequiredAccPlaceTitle}' not found on target database.");
            return null;
        }

        var company = await db.Companies.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        var contact = await db.Contacts.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);

        if (planningJobTypeId == 0 || company is null || contact is null)
        {
            evidence.Fail(
                "cert.prp.preconditions",
                "Target database is missing planning job type mapping, company or contact rows.");
            return null;
        }

        evidence.Pass(
            "cert.prp.preconditions",
            $"Proposal definition {proposal.Id}, planning job type {planningJobTypeId}, place {place.Id}, "
            + $"company {company.Id}, contact {contact.Id}.");

        return new Preconditions(
            proposal.Id,
            place.Id,
            company.Id,
            contact.Id,
            planningJobTypeId);
    }

    internal static Task<CorridorInbox?> TryResolveInboxForCreatePriceQuoteAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int proposalDefinitionId,
        SystemCertificationEnvironment.GmailLayer gmail,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default) =>
        SystemCertificationPrpSourceEmail.TryResolveExplicitSourceAsync(
            provider,
            dbFactory,
            proposalDefinitionId,
            gmail,
            evidence,
            cancellationToken);

    internal static async Task<int> ExecuteCreatePriceQuoteAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        CorridorInbox inbox,
        int proposalDefinitionId,
        int operatorUserId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        var execution = provider.GetRequiredService<IEmailSuggestedActionExecutionService>();
        var result = await execution.ExecuteAsync(
            new EmailSuggestedActionExecutionCommand(
                EmailSuggestedActionCodes.CreatePriceQuote,
                inbox.InboxMessageId,
                operatorUserId,
                inbox.GmailSource),
            cancellationToken);

        if (!result.Succeeded && result.WorkflowInstanceId is not int reused)
        {
            evidence.Fail("cert.prp.create_price_quote", $"CreatePriceQuote failed: {result.Message}");
            return 0;
        }

        var instanceId = result.WorkflowInstanceId
            ?? await FindActiveProposalInstanceForInboxAsync(
                dbFactory,
                proposalDefinitionId,
                result.InboxMessageId ?? inbox.InboxMessageId,
                cancellationToken);

        if (instanceId <= 0)
        {
            evidence.Fail("cert.prp.create_price_quote", "CreatePriceQuote did not produce a workflow instance.");
            return 0;
        }

        evidence.Pass(
            "cert.prp.create_price_quote",
            result.Succeeded
                ? $"{WorkflowCodes.Proposal} instance id={instanceId} at {ProposalStageCodes.ProjectSetup} "
                  + $"({inbox.SelectionDetail})."
                : $"{WorkflowCodes.Proposal} instance id={instanceId} reused ({result.Message}).");
        evidence.Created("WorkflowInstance", instanceId.ToString(), WorkflowCodes.Proposal);
        if (result.InboxMessageId is int inboxId)
        {
            evidence.Created("EmailInboxMessage", inboxId.ToString(), "CreatePriceQuote materialized inbox row");
        }

        return instanceId;
    }

    internal static async Task<int> CreateCertProjectAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        Preconditions pre,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        var creator = new SqlProjectCreateService(dbFactory);
        var title = $"{SystemCertificationEnvironment.CertificationTitlePrefix} {DateTime.Now:MMdd-HHmm}";

        var result = await creator.CreateAsync(
            new CreateProjectCommand(
                Title: title,
                PlaceId: pre.PlaceId,
                CompanyId: pre.CompanyId,
                ContactId: pre.ContactId,
                JobTypeIds: [pre.PlanningJobTypeId]),
            cancellationToken);

        if (!result.Succeeded || result.ProjectId is not int projectId)
        {
            evidence.Fail("cert.prp.project", $"Cert project creation failed: {result.ErrorMessage}");
            return 0;
        }

        evidence.Pass(
            "cert.prp.project",
            $"id={projectId} title='{result.ProjectTitle}' place='{result.PlaceTitle}'.");
        evidence.Created("Project", projectId.ToString(), title);
        return projectId;
    }

    internal static async Task<bool> WalkCorridorUntilSendQuoteAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationHost.SystemCertificationRunContext context,
        SystemCertificationEvidence evidence,
        int instanceId,
        int certProjectId,
        int operatorUserId,
        CancellationToken cancellationToken = default)
    {
        var navigation = provider.GetRequiredService<ITaskNavigationService>();
        var completion = provider.GetRequiredService<ITaskCompletionService>();

        for (var step = 1; step <= 16; step++)
        {
            var openTasks = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
                dbFactory, instanceId, cancellationToken);
            if (openTasks.Count == 0)
            {
                evidence.Fail(
                    "cert.prp.corridor",
                    $"No open driving task on instance {instanceId} after {step - 1} completion(s).");
                return false;
            }

            if (openTasks.Count > 1)
            {
                evidence.Fail(
                    "cert.prp.corridor",
                    $"Instance {instanceId} has {openTasks.Count} open driving tasks before step {step}.");
                return false;
            }

            var open = openTasks[0];
            if (string.Equals(open.TaskTypeCode, TaskTypeCodes.SendQuoteToClient, StringComparison.Ordinal))
            {
                evidence.Pass(
                    "cert.prp.corridor",
                    $"Reached {TaskTypeCodes.SendQuoteToClient} (task id={open.TaskId}) after "
                    + $"{step - 1} completion(s) through production seams only.");
                return true;
            }

            var completed = await TryCompleteTaskAsync(
                provider,
                dbFactory,
                integrity,
                context,
                navigation,
                completion,
                open,
                instanceId,
                certProjectId,
                operatorUserId,
                evidence,
                cancellationToken);

            if (!completed)
            {
                return false;
            }

            if (string.Equals(open.TaskTypeCode, TaskTypeCodes.FileQuoteMaterial, StringComparison.Ordinal))
            {
                continue;
            }

            var expectedStage = SystemCertificationTransitionAssertions.ExpectedStageAfterTask(open.TaskTypeCode);
            if (expectedStage is null)
            {
                evidence.Fail(
                    "cert.prp.corridor",
                    $"No expected stage mapping for completed task type '{open.TaskTypeCode}'.");
                return false;
            }

            await SystemCertificationTransitionAssertions.AssertAfterTransitionAsync(
                dbFactory,
                integrity,
                evidence,
                $"cert.prp.transition.{open.TaskTypeCode}",
                instanceId,
                open.TaskId,
                expectedStage,
                cancellationToken);
        }

        evidence.Fail(
            "cert.prp.corridor",
            "Completed 16 tasks without reaching SendQuoteToClient.");
        return false;
    }

    private static async Task<bool> TryCompleteTaskAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationHost.SystemCertificationRunContext context,
        ITaskNavigationService navigation,
        ITaskCompletionService completion,
        SystemCertificationTransitionAssertions.OpenDrivingTask open,
        int instanceId,
        int certProjectId,
        int operatorUserId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        if (string.Equals(open.TaskTypeCode, TaskTypeCodes.OpenQuoteProject, StringComparison.Ordinal))
        {
            return await TryCompleteOpenQuoteProjectAsync(
                provider, dbFactory, open.TaskId, certProjectId, operatorUserId, evidence);
        }

        if (string.Equals(open.TaskTypeCode, TaskTypeCodes.FileQuoteMaterial, StringComparison.Ordinal))
        {
            return await SystemCertificationPrpFileMaterialProof.TryProveAndCompleteAsync(
                provider,
                dbFactory,
                integrity,
                context,
                evidence,
                open.TaskId,
                certProjectId,
                instanceId,
                operatorUserId,
                cancellationToken);
        }

        var navigationContext = await navigation.ResolveAsync(open.TaskId, cancellationToken);
        if (navigationContext?.CompletionEventCode is not { Length: > 0 } eventCode)
        {
            evidence.Fail(
                "cert.prp.corridor",
                $"Task {open.TaskId} ({open.TaskTypeCode}) has no resolvable CompletionEventCode.");
            return false;
        }

        var resultCode = ChooseResultCode(open.TaskTypeCode, navigationContext.AllowedResultCodes);
        if (resultCode is null && navigationContext.AllowedResultCodes.Count > 0)
        {
            evidence.Fail(
                "cert.prp.corridor",
                $"Task {open.TaskId} ({open.TaskTypeCode}) offers ambiguous results ["
                + string.Join(", ", navigationContext.AllowedResultCodes)
                + "] with no declared happy-path choice.");
            return false;
        }

        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(open.TaskId, eventCode, resultCode, null, operatorUserId),
            cancellationToken);

        if (!outcome.Success)
        {
            evidence.Fail(
                "cert.prp.corridor",
                $"Task {open.TaskId} ({open.TaskTypeCode}) completion refused: {Trim(outcome.ErrorMessage)}.");
            return false;
        }

        return true;
    }

    private static async Task<bool> TryCompleteOpenQuoteProjectAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int taskId,
        int certProjectId,
        int operatorUserId,
        SystemCertificationEvidence evidence)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await db.ProjectAssignments
            .Include(t => t.TaskLinks)
            .FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null)
        {
            evidence.Fail("cert.prp.corridor", $"OpenQuoteProject task {taskId} not found.");
            return false;
        }

        var emailLink = task.TaskLinks.FirstOrDefault(
            l => l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage);
        if (emailLink is not null)
        {
            var inbox = await db.EmailInboxMessages
                .FirstOrDefaultAsync(m => m.Id == (int)emailLink.LinkedEntityId);
            if (inbox is not null)
            {
                inbox.ProjectId = certProjectId;
                await db.SaveChangesAsync();
            }
        }

        var decision = provider.GetRequiredService<IOpenQuoteProjectDecisionService>();
        var outcome = await decision.CompleteDecisionAsync(
            new OpenQuoteProjectDecisionCommand(
                taskId,
                operatorUserId,
                ReviewCompletionEvents.ReviewProjectCreated,
                TaskResultCodes.ProjectOpened),
            CancellationToken.None);

        if (!outcome.Success)
        {
            evidence.Fail(
                "cert.prp.corridor",
                $"OpenQuoteProject completion refused: {Trim(outcome.ErrorMessage)}.");
            return false;
        }

        return true;
    }

    private static string? ChooseResultCode(string taskTypeCode, IReadOnlyList<string> allowed)
    {
        if (allowed.Count == 0)
        {
            return null;
        }

        if (DesiredResultByTaskType.TryGetValue(taskTypeCode, out var desired)
            && allowed.Contains(desired, StringComparer.Ordinal))
        {
            return desired;
        }

        return allowed.Count == 1 ? allowed[0] : null;
    }

    private static async Task<int> FindActiveProposalInstanceForInboxAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int proposalDefinitionId,
        int? inboxMessageId,
        CancellationToken cancellationToken)
    {
        if (inboxMessageId is not int inboxId || inboxId <= 0)
        {
            return 0;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.WorkflowInstances.AsNoTracking()
            .Where(w => w.WorkflowDefinitionId == proposalDefinitionId
                        && w.TriggerType == WorkflowTriggerType.Email
                        && w.TriggerEntityId == inboxId
                        && w.Status != WorkflowStatus.Completed
                        && w.Status != WorkflowStatus.Cancelled)
            .OrderByDescending(w => w.Id)
            .Select(w => w.Id)
            .FirstOrDefaultAsync(cancellationToken);
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
