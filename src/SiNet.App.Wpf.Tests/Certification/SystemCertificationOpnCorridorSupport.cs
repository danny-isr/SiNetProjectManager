using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// OPN corridor for the certification tier. Starts only through
/// <see cref="IEmailSuggestedActionExecutionService"/> / CreateOpinionProject,
/// rebinds the inbox to a disposable [SYS-CERT] project (Opinion is project-bound),
/// and walks production seams until open SendOpinion (outbound send remains policy-blocked).
/// </summary>
internal static class SystemCertificationOpnCorridorSupport
{
    internal sealed record Preconditions(
        int OpinionDefinitionId,
        int PlaceId,
        int CompanyId,
        int ContactId,
        int PlanningJobTypeId);

    private static readonly Dictionary<string, string> DesiredResultByTaskType = new(StringComparer.Ordinal)
    {
        [TaskTypeCodes.AnalyzeOpinionMaterials] = TaskResultCodes.OpinionAnalysisCompleted,
        [TaskTypeCodes.PrepareOpinionDraft] = TaskResultCodes.OpinionDraftPrepared,
        [TaskTypeCodes.ReviewOpinionInternal] = TaskResultCodes.OpinionApprovedInternally,
    };

    internal static async Task<Preconditions?> TryResolvePreconditionsAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var opinion = await db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == WorkflowCodes.Opinion && d.IsActive, cancellationToken);
        if (opinion is null)
        {
            evidence.Fail("cert.opn.preconditions", "Active Opinion workflow definition not found.");
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
                "cert.opn.preconditions",
                $"Active {WorkflowCodes.PlanningWorkflow} definition not found "
                + "(needed only to pick a JobType for the disposable [SYS-CERT] project).");
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
                "cert.opn.preconditions",
                $"Place '{SystemCertificationEnvironment.RequiredAccPlaceTitle}' not found on target database.");
            return null;
        }

        var company = await db.Companies.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        var contact = await db.Contacts.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);

        if (planningJobTypeId == 0 || company is null || contact is null)
        {
            evidence.Fail(
                "cert.opn.preconditions",
                "Target database is missing planning job type mapping, company or contact rows.");
            return null;
        }

        evidence.Pass(
            "cert.opn.preconditions",
            $"Opinion definition {opinion.Id}, planning job type {planningJobTypeId}, place {place.Id}, "
            + $"company {company.Id}, contact {contact.Id}.");

        return new Preconditions(
            opinion.Id,
            place.Id,
            company.Id,
            contact.Id,
            planningJobTypeId);
    }

    internal static async Task<int> CreateCertProjectAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        Preconditions pre,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        var creator = new SqlProjectCreateService(dbFactory);
        var title = $"{SystemCertificationEnvironment.CertificationTitlePrefix} {DateTime.Now:MMdd-HHmmss-fff}";

        var result = await creator.CreateAsync(
            new CreateProjectCommand(
                Title: title,
                PlaceId: pre.PlaceId,
                CompanyId: pre.CompanyId,
                ContactId: pre.ContactId,
                // Create requires ≥1 JobType; Opinion has no ProjectTypeWorkflowDefinition seed
                // mapping, so we strip the link immediately after create (open policy).
                JobTypeIds: [pre.PlanningJobTypeId]),
            cancellationToken);

        if (!result.Succeeded || result.ProjectId is not int projectId)
        {
            evidence.Fail("cert.opn.project", $"Cert project creation failed: {result.ErrorMessage}");
            return 0;
        }

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var typeRows = await db.TypeOfProjectInProjects
                .Where(t => t.ProjectId == projectId)
                .ToListAsync(cancellationToken);
            if (typeRows.Count > 0)
            {
                db.TypeOfProjectInProjects.RemoveRange(typeRows);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        evidence.Pass(
            "cert.opn.project",
            $"id={projectId} title='{result.ProjectTitle}' place='{result.PlaceTitle}' "
            + "(JobTypes cleared after create for Opinion open-policy start).");
        evidence.Created("Project", projectId.ToString(), title);
        return projectId;
    }

    /// <summary>
    /// Opinion is project-bound on inbox.ProjectId. Rebind away from the office default
    /// onto the disposable [SYS-CERT] project before CreateOpinionProject.
    /// </summary>
    internal static async Task<bool> RebindInboxToCertProjectAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int inboxMessageId,
        int certProjectId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var inbox = await db.EmailInboxMessages.FirstOrDefaultAsync(m => m.Id == inboxMessageId, cancellationToken);
        if (inbox is null)
        {
            evidence.Fail("cert.opn.rebind", $"EmailInboxMessage id={inboxMessageId} not found for OPN rebind.");
            return false;
        }

        var previous = inbox.ProjectId;
        inbox.ProjectId = certProjectId;
        await db.SaveChangesAsync(cancellationToken);

        evidence.Pass(
            "cert.opn.rebind",
            $"Inbox {inboxMessageId} ProjectId {previous} → {certProjectId} before CreateOpinionProject.");
        return true;
    }

    internal static async Task<int> ExecuteCreateOpinionProjectAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationPrpCorridorSupport.CorridorInbox inbox,
        int opinionDefinitionId,
        int operatorUserId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(evidence);
        _ = opinionDefinitionId;

        if (inbox.InboxMessageId is not int inboxMessageId || inboxMessageId <= 0)
        {
            evidence.Fail(
                "cert.opn.create_opinion_project",
                "CreateOpinionProject requires a fully ingested EmailInboxMessage for certification proof.");
            return 0;
        }

        var execution = provider.GetRequiredService<IEmailSuggestedActionExecutionService>();
        var result = await execution.ExecuteAsync(
            new EmailSuggestedActionExecutionCommand(
                EmailSuggestedActionCodes.CreateOpinionProject,
                inboxMessageId,
                operatorUserId,
                GmailSource: null),
            cancellationToken);

        // Certification requires a fresh CreateOpinionProject start. Duplicate-guard reuse
        // (Succeeded=false with an existing WorkflowInstanceId) is FAIL — mid-corridor leftovers
        // must be cancelled (WorkflowStatus.Cancelled=4), not treated as a soft pass.
        if (!result.Succeeded)
        {
            evidence.Fail(
                "cert.opn.create_opinion_project",
                $"CreateOpinionProject did not start a new instance: {result.Message}"
                + (result.WorkflowInstanceId is int reusedId
                    ? $" (existing instance #{reusedId})."
                    : string.Empty));
            return 0;
        }

        if (result.WorkflowInstanceId is not int instanceId || instanceId <= 0)
        {
            evidence.Fail(
                "cert.opn.create_opinion_project",
                "CreateOpinionProject succeeded but did not return a workflow instance id.");
            return 0;
        }

        evidence.Pass(
            "cert.opn.create_opinion_project",
            $"{WorkflowCodes.Opinion} instance id={instanceId} ({inbox.SelectionDetail}).");
        evidence.Created("WorkflowInstance", instanceId.ToString(), WorkflowCodes.Opinion);
        return instanceId;
    }

    internal static async Task<bool> WalkCorridorUntilSendOpinionAsync(
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

        for (var i = 0; i < 12; i++)
        {
            var openTasks = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
                dbFactory, instanceId, cancellationToken);
            if (openTasks.Count == 0)
            {
                evidence.Fail("cert.opn.corridor", $"No open driving tasks at corridor step {i}.");
                return false;
            }

            if (openTasks.Count > 1)
            {
                evidence.Fail(
                    "cert.opn.corridor",
                    $"Expected a single open driving task; got "
                    + string.Join(", ", openTasks.Select(t => $"{t.TaskTypeCode}#{t.TaskId}")));
                return false;
            }

            var open = openTasks[0];
            if (string.Equals(open.TaskTypeCode, TaskTypeCodes.SendOpinion, StringComparison.Ordinal))
            {
                evidence.Pass(
                    "cert.opn.corridor",
                    $"Reached open {TaskTypeCodes.SendOpinion} at policy boundary.");
                return true;
            }

            if (string.Equals(open.TaskTypeCode, TaskTypeCodes.FileInitialMaterials, StringComparison.Ordinal))
            {
                var filed = await SystemCertificationPrpFileMaterialProof.TryProveAndCompleteAsync(
                    provider,
                    dbFactory,
                    integrity,
                    context,
                    evidence,
                    open.TaskId,
                    certProjectId,
                    instanceId,
                    operatorUserId,
                    cancellationToken,
                    SystemCertificationPrpFileMaterialProof.FilingEvidenceSteps.Opn,
                    OpinionStageCodes.AnalyzeDocuments);
                if (!filed)
                {
                    return false;
                }

                continue;
            }

            var navigationContext = await navigation.ResolveAsync(open.TaskId, cancellationToken);
            string? eventCode;
            string? resultCode;
            if (TryResolveOpnCompletion(open.TaskTypeCode, out var opnEvent, out var opnResult))
            {
                eventCode = opnEvent;
                resultCode = opnResult;
            }
            else
            {
                if (navigationContext?.CompletionEventCode is not { Length: > 0 } resolvedEvent)
                {
                    evidence.Fail(
                        "cert.opn.corridor",
                        $"Task {open.TaskId} ({open.TaskTypeCode}) has no resolvable CompletionEventCode.");
                    return false;
                }

                eventCode = resolvedEvent;
                resultCode = ChooseResultCode(open.TaskTypeCode, navigationContext.AllowedResultCodes);
                if (resultCode is null && navigationContext.AllowedResultCodes.Count > 0)
                {
                    evidence.Fail(
                        "cert.opn.corridor",
                        $"Task {open.TaskId} ({open.TaskTypeCode}) offers ambiguous results ["
                        + string.Join(", ", navigationContext.AllowedResultCodes)
                        + "] with no declared happy-path choice.");
                    return false;
                }
            }

            var outcome = await completion.CompleteAsync(
                new CompleteTaskCommand(open.TaskId, eventCode, resultCode, null, operatorUserId),
                cancellationToken);

            if (!outcome.Success)
            {
                evidence.Fail(
                    "cert.opn.corridor",
                    $"Task {open.TaskId} ({open.TaskTypeCode}) completion refused: {Trim(outcome.ErrorMessage)}.");
                return false;
            }

            // Soft-success trap: CompleteAsync can record a result with Success=true while
            // leaving the task open (e.g. work-targets-pending without ClosesAssociatedTask).
            if (!outcome.TaskClosed)
            {
                evidence.Fail(
                    "cert.opn.corridor",
                    $"Task {open.TaskId} ({open.TaskTypeCode}) reported Success without TaskClosed "
                    + $"(result={outcome.RecordedTaskResultCode ?? "(none)"}).");
                return false;
            }

            var expectedStage = SystemCertificationTransitionAssertions.ExpectedOpnStageAfterTask(open.TaskTypeCode);
            if (expectedStage is null)
            {
                evidence.Fail(
                    "cert.opn.corridor",
                    $"No expected stage mapping for completed task type '{open.TaskTypeCode}'.");
                return false;
            }

            if (!await SystemCertificationTransitionAssertions.AssertAfterTransitionAsync(
                    dbFactory,
                    integrity,
                    evidence,
                    $"cert.opn.transition.{open.TaskTypeCode}",
                    instanceId,
                    open.TaskId,
                    expectedStage,
                    cancellationToken))
            {
                return false;
            }
        }

        evidence.Fail(
            "cert.opn.corridor",
            "Completed 12 tasks without reaching SendOpinion.");
        return false;
    }

    private static bool TryResolveOpnCompletion(
        string taskTypeCode,
        out string eventCode,
        out string resultCode)
    {
        switch (taskTypeCode)
        {
            case TaskTypeCodes.AnalyzeOpinionMaterials:
                eventCode = ReviewCompletionEvents.AnalysisCompleted;
                resultCode = TaskResultCodes.OpinionAnalysisCompleted;
                return true;
            case TaskTypeCodes.PrepareOpinionDraft:
                eventCode = ReviewCompletionEvents.DraftPrepared;
                resultCode = TaskResultCodes.OpinionDraftPrepared;
                return true;
            case TaskTypeCodes.ReviewOpinionInternal:
                eventCode = ReviewCompletionEvents.InternalReviewCompleted;
                resultCode = TaskResultCodes.OpinionApprovedInternally;
                return true;
            default:
                eventCode = string.Empty;
                resultCode = string.Empty;
                return false;
        }
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
