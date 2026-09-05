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
/// REV corridor for certification. Starts through <see cref="IEmailSuggestedActionExecutionService"/> /
/// CreateNewReview, completes OpenReviewProject, walks the hosted MaterialIntake child through ACC filing,
/// then walks the no-police happy path to REV.Completed.
/// </summary>
internal static class SystemCertificationRevCorridorSupport
{
    internal sealed record Preconditions(
        int ReviewDefinitionId,
        int MaterialIntakeDefinitionId,
        int PlaceId,
        int CompanyId,
        int ContactId,
        int PlanningJobTypeId);

    private const string MatChildStep = "cert.rev.mat.child";
    private const string MatCorridorStep = "cert.rev.mat.corridor";
    private const string MatCheckStep = "cert.rev.mat.transition.CheckQuoteMaterialCompleteness";
    private static readonly SystemCertificationPrpFileMaterialProof.FilingEvidenceSteps MatFilingSteps =
        SystemCertificationPrpFileMaterialProof.FilingEvidenceSteps.Rev;

    private static readonly Dictionary<string, (string EventCode, string ResultCode)> RevCompletionByTask =
        new(StringComparer.Ordinal)
        {
            [TaskTypeCodes.PerformProfessionalReview] =
                (ReviewCompletionEvents.ReviewProfessionalReviewCompleted, TaskResultCodes.ProfessionalReviewCompleted),
            [TaskTypeCodes.ApproveReviewReport] =
                (ReviewCompletionEvents.ReviewManagerApproved, TaskResultCodes.ManagerApproved),
            [TaskTypeCodes.SendReportToPlanner] =
                (ReviewCompletionEvents.ReviewCommentsSentToPlanner, TaskResultCodes.CommentsSentToPlanner),
            [TaskTypeCodes.TrackPlannerCorrections] =
                (ReviewCompletionEvents.ReviewPlannerCorrectionsReceived, TaskResultCodes.PlannerCorrectionsReceived),
            [TaskTypeCodes.RecheckPlan] =
                (ReviewCompletionEvents.ReviewRecheckPassed, TaskResultCodes.RecheckPassed),
            [TaskTypeCodes.DeterminePoliceApprovalRequirement] =
                (ReviewCompletionEvents.ReviewPoliceRequirementDecided, TaskResultCodes.PoliceApprovalNotRequired),
            [TaskTypeCodes.CloseProject] =
                (ReviewCompletionEvents.ProjectCloseDecided, TaskResultCodes.ProjectCloseApproved),
        };

    internal static async Task<Preconditions?> TryResolvePreconditionsAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var review = await db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == WorkflowCodes.Review && d.IsActive, cancellationToken);
        if (review is null)
        {
            evidence.Fail("cert.rev.preconditions", "Active Review workflow definition not found.");
            return null;
        }

        var material = await db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == WorkflowCodes.MaterialIntake && d.IsActive, cancellationToken);
        if (material is null)
        {
            evidence.Fail("cert.rev.preconditions", "Active MaterialIntake definition not found.");
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
                "cert.rev.preconditions",
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
                "cert.rev.preconditions",
                $"Place '{SystemCertificationEnvironment.RequiredAccPlaceTitle}' not found on target database.");
            return null;
        }

        var company = await db.Companies.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        var contact = await db.Contacts.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);

        if (planningJobTypeId == 0 || company is null || contact is null)
        {
            evidence.Fail(
                "cert.rev.preconditions",
                "Target database is missing planning job type mapping, company or contact rows.");
            return null;
        }

        evidence.Pass(
            "cert.rev.preconditions",
            $"Review definition {review.Id}, MaterialIntake {material.Id}, "
            + $"job type {planningJobTypeId}, place {place.Id}, company {company.Id}, contact {contact.Id}.");

        return new Preconditions(
            review.Id,
            material.Id,
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
        var title = $"{SystemCertificationEnvironment.CertificationTitlePrefix} {DateTime.Now:yyMMdd-HHmmss}";

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
            evidence.Fail("cert.rev.project", $"Cert project creation failed: {result.ErrorMessage}");
            return 0;
        }

        evidence.Pass(
            "cert.rev.project",
            $"id={projectId} title='{result.ProjectTitle}' place='{result.PlaceTitle}'.");
        evidence.Created("Project", projectId.ToString(), title);
        return projectId;
    }

    internal static async Task<int> ExecuteCreateNewReviewAsync(
        IServiceProvider provider,
        SystemCertificationPrpCorridorSupport.CorridorInbox inbox,
        int operatorUserId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(evidence);

        if (inbox.InboxMessageId is not int inboxMessageId || inboxMessageId <= 0)
        {
            evidence.Fail(
                "cert.rev.create_new_review",
                "CreateNewReview requires a fully ingested EmailInboxMessage for certification proof.");
            return 0;
        }

        var execution = provider.GetRequiredService<IEmailSuggestedActionExecutionService>();
        var result = await execution.ExecuteAsync(
            new EmailSuggestedActionExecutionCommand(
                EmailSuggestedActionCodes.CreateNewReview,
                inboxMessageId,
                operatorUserId,
                GmailSource: null),
            cancellationToken);

        if (!result.Succeeded)
        {
            evidence.Fail(
                "cert.rev.create_new_review",
                $"CreateNewReview did not start a new instance: {result.Message}"
                + (result.WorkflowInstanceId is int reusedId
                    ? $" (existing instance #{reusedId})."
                    : string.Empty));
            return 0;
        }

        if (result.WorkflowInstanceId is not int instanceId || instanceId <= 0)
        {
            evidence.Fail(
                "cert.rev.create_new_review",
                "CreateNewReview succeeded but did not return a workflow instance id.");
            return 0;
        }

        evidence.Pass(
            "cert.rev.create_new_review",
            $"{WorkflowCodes.Review} instance id={instanceId} at {ReviewStageCodes.ProjectSetup} "
            + $"({inbox.SelectionDetail}).");
        evidence.Created("WorkflowInstance", instanceId.ToString(), WorkflowCodes.Review);
        return instanceId;
    }

    internal static async Task<bool> TryCompleteOpenReviewProjectAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int taskId,
        int certProjectId,
        int operatorUserId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var task = await db.ProjectAssignments
            .Include(t => t.TaskLinks)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            evidence.Fail("cert.rev.transition.OpenReviewProject", $"OpenReviewProject task {taskId} missing.");
            return false;
        }

        var emailLink = task.TaskLinks.FirstOrDefault(
            l => l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage);
        if (emailLink is not null)
        {
            var inbox = await db.EmailInboxMessages
                .FirstOrDefaultAsync(m => m.Id == (int)emailLink.LinkedEntityId, cancellationToken);
            if (inbox is not null)
            {
                inbox.ProjectId = certProjectId;
                await db.SaveChangesAsync(cancellationToken);
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
                "cert.rev.transition.OpenReviewProject",
                $"OpenReviewProject completion refused: {Trim(outcome.ErrorMessage)}.");
            return false;
        }

        evidence.Pass(
            "cert.rev.transition.OpenReviewProject",
            $"closed task {taskId}; cert project {certProjectId} bound; parent advanced toward {ReviewStageCodes.MaterialIntake}.");
        return true;
    }

    internal static async Task<(bool Success, int MatInstanceId)> WalkMatAndRevHappyPathAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationHost.SystemCertificationRunContext context,
        SystemCertificationEvidence evidence,
        Preconditions pre,
        int parentInstanceId,
        int certProjectId,
        int operatorUserId,
        CancellationToken cancellationToken = default)
    {
        var matSteps = SystemCertificationPlnCorridorSupport.EvidenceSteps.Mat with
        {
            MatChild = MatChildStep,
            MatCorridor = MatCorridorStep,
            MatCheck = MatCheckStep,
            Filing = MatFilingSteps,
        };

        var matInstanceId = await SystemCertificationPlnCorridorSupport.WaitForMatChildAsync(
            dbFactory,
            pre.MaterialIntakeDefinitionId,
            parentInstanceId,
            evidence,
            MatChildStep,
            cancellationToken);
        if (matInstanceId <= 0)
        {
            return (false, 0);
        }

        if (!await SystemCertificationPlnCorridorSupport.WalkMatChildAsync(
                provider,
                dbFactory,
                integrity,
                context,
                evidence,
                matSteps,
                matInstanceId,
                certProjectId,
                operatorUserId,
                cancellationToken))
        {
            return (false, matInstanceId);
        }

        if (!await WaitForParentProfessionalReviewAsync(
                dbFactory, parentInstanceId, evidence, cancellationToken))
        {
            return (false, matInstanceId);
        }

        var walked = await WalkRevDrivingTasksAsync(
            provider,
            dbFactory,
            integrity,
            evidence,
            parentInstanceId,
            certProjectId,
            operatorUserId,
            cancellationToken);
        return (walked, matInstanceId);
    }

    internal static async Task<bool> AssertTerminalCompletedAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationEvidence evidence,
        int reviewInstanceId,
        int matInstanceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var review = await db.WorkflowInstances.AsNoTracking()
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.Id == reviewInstanceId, cancellationToken);
        if (review is null)
        {
            evidence.Fail("cert.rev.terminal", $"Review instance {reviewInstanceId} missing.");
            return false;
        }

        if (review.Status != WorkflowStatus.Completed)
        {
            evidence.Fail(
                "cert.rev.terminal",
                $"Review instance {reviewInstanceId} status={review.Status}, expected Completed.");
            return false;
        }

        if (!string.Equals(review.CurrentStage?.Code, ReviewStageCodes.Completed, StringComparison.Ordinal))
        {
            evidence.Fail(
                "cert.rev.terminal",
                $"Review instance stage='{review.CurrentStage?.Code ?? "<null>"}', expected {ReviewStageCodes.Completed}.");
            return false;
        }

        var openReviewTasks = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
            dbFactory, reviewInstanceId, cancellationToken);
        if (openReviewTasks.Count > 0)
        {
            evidence.Fail(
                "cert.rev.terminal",
                $"Review instance has {openReviewTasks.Count} open driving task(s): "
                + string.Join(", ", openReviewTasks.Select(t => $"{t.TaskTypeCode}#{t.TaskId}")));
            return false;
        }

        var mat = await db.WorkflowInstances.AsNoTracking()
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.Id == matInstanceId, cancellationToken);
        if (mat is null)
        {
            evidence.Fail("cert.rev.mat.terminal", $"MAT child instance {matInstanceId} missing.");
            return false;
        }

        if (mat.Status != WorkflowStatus.Completed)
        {
            evidence.Fail(
                "cert.rev.mat.terminal",
                $"MAT child {matInstanceId} status={mat.Status}, expected Completed.");
            return false;
        }

        var openMatTasks = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
            dbFactory, matInstanceId, cancellationToken);
        if (openMatTasks.Count > 0)
        {
            evidence.Fail(
                "cert.rev.mat.terminal",
                $"MAT child has {openMatTasks.Count} open driving task(s).");
            return false;
        }

        evidence.Pass(
            "cert.rev.terminal",
            $"Review instance {reviewInstanceId} Completed; zero open REV driving tasks.");
        evidence.Pass(
            "cert.rev.mat.terminal",
            $"MAT child {matInstanceId} terminal with zero open tasks.");
        return true;
    }

    private static async Task<bool> WaitForParentProfessionalReviewAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int parentInstanceId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var stage = await SystemCertificationPlnCorridorSupport.ReadStageCodeAsync(
                dbFactory, parentInstanceId, cancellationToken);
            var open = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
                dbFactory, parentInstanceId, cancellationToken);
            if (string.Equals(stage, ReviewStageCodes.ProfessionalReview, StringComparison.Ordinal)
                && open.Count == 1
                && string.Equals(open[0].TaskTypeCode, TaskTypeCodes.PerformProfessionalReview, StringComparison.Ordinal))
            {
                evidence.Pass(
                    "cert.rev.transition.MaterialIntake",
                    $"MAT child complete; parent at {ReviewStageCodes.ProfessionalReview} "
                    + $"with open {TaskTypeCodes.PerformProfessionalReview}#{open[0].TaskId}.");
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        var finalStage = await SystemCertificationPlnCorridorSupport.ReadStageCodeAsync(
            dbFactory, parentInstanceId, cancellationToken);
        evidence.Fail(
            "cert.rev.transition.MaterialIntake",
            $"After MAT complete expected {ReviewStageCodes.ProfessionalReview} + open "
            + $"{TaskTypeCodes.PerformProfessionalReview}; got stage='{finalStage ?? "<null>"}'.");
        return false;
    }

    private static async Task<bool> WalkRevDrivingTasksAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationEvidence evidence,
        int instanceId,
        int certProjectId,
        int operatorUserId,
        CancellationToken cancellationToken)
    {
        var completion = provider.GetRequiredService<ITaskCompletionService>();
        var sharedReportId = 0;

        foreach (var taskType in SystemCertificationTransitionAssertions.RevHappyPathTaskTypes)
        {
            if (string.Equals(taskType, TaskTypeCodes.OpenReviewProject, StringComparison.Ordinal))
            {
                continue;
            }

            var openTasks = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
                dbFactory, instanceId, cancellationToken);
            if (openTasks.Count != 1
                || !string.Equals(openTasks[0].TaskTypeCode, taskType, StringComparison.Ordinal))
            {
                evidence.Fail(
                    $"cert.rev.transition.{taskType}",
                    $"Expected one open {taskType}; found "
                    + (openTasks.Count == 0
                        ? "none"
                        : string.Join(", ", openTasks.Select(t => $"{t.TaskTypeCode}#{t.TaskId}"))));
                return false;
            }

            var open = openTasks[0];
            var step = $"cert.rev.transition.{taskType}";

            if (string.Equals(taskType, TaskTypeCodes.CloseProject, StringComparison.Ordinal))
            {
                if (!RevCompletionByTask.TryGetValue(taskType, out var closePair))
                {
                    evidence.Fail(step, $"No completion mapping for {taskType}.");
                    return false;
                }

                (sharedReportId, var closeLinkIds) = await SystemCertificationRevInspectionProof
                    .ResolveCompletedWorkTargetLinkIdsAsync(
                        dbFactory,
                        open.TaskId,
                        taskType,
                        certProjectId,
                        operatorUserId,
                        sharedReportId,
                        cancellationToken);

                var closeOutcome = await completion.CompleteAsync(
                    new CompleteTaskCommand(
                        open.TaskId,
                        closePair.EventCode,
                        closePair.ResultCode,
                        closeLinkIds.Count > 0 ? closeLinkIds : null,
                        operatorUserId),
                    cancellationToken);

                if (!closeOutcome.Success || !closeOutcome.TaskClosed)
                {
                    evidence.Fail(
                        step,
                        $"CloseProject #{open.TaskId} failed: Success={closeOutcome.Success} "
                        + $"TaskClosed={closeOutcome.TaskClosed} err={Trim(closeOutcome.ErrorMessage)}");
                    return false;
                }

                for (var wait = 0; wait < 40; wait++)
                {
                    var stage = await SystemCertificationPlnCorridorSupport.ReadStageCodeAsync(
                        dbFactory, instanceId, cancellationToken);
                    var afterOpen = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
                        dbFactory, instanceId, cancellationToken);
                    if (string.Equals(stage, ReviewStageCodes.Completed, StringComparison.Ordinal)
                        && afterOpen.Count == 0)
                    {
                        var report = await integrity.CheckAsync(cancellationToken);
                        if (!report.IsDeltaClean)
                        {
                            evidence.Fail(step, report.DescribeDelta());
                            return false;
                        }

                        evidence.Pass(
                            step,
                            $"closed task {open.TaskId}; stage {ReviewStageCodes.Completed}; zero open tasks; delta clean.");
                        return true;
                    }

                    await Task.Delay(250, cancellationToken);
                }

                evidence.Fail(step, $"After CloseProject expected {ReviewStageCodes.Completed} with zero open tasks.");
                return false;
            }

            if (!RevCompletionByTask.TryGetValue(taskType, out var pair))
            {
                evidence.Fail(step, $"No completion mapping for {taskType}.");
                return false;
            }

            (sharedReportId, var completedLinkIds) = await SystemCertificationRevInspectionProof
                .ResolveCompletedWorkTargetLinkIdsAsync(
                    dbFactory,
                    open.TaskId,
                    taskType,
                    certProjectId,
                    operatorUserId,
                    sharedReportId,
                    cancellationToken);

            var outcome = await completion.CompleteAsync(
                new CompleteTaskCommand(
                    open.TaskId,
                    pair.EventCode,
                    pair.ResultCode,
                    completedLinkIds.Count > 0 ? completedLinkIds : null,
                    operatorUserId),
                cancellationToken);

            if (!outcome.Success || !outcome.TaskClosed)
            {
                evidence.Fail(
                    step,
                    $"{taskType} #{open.TaskId} failed: Success={outcome.Success} "
                    + $"TaskClosed={outcome.TaskClosed} err={Trim(outcome.ErrorMessage)}");
                return false;
            }

            var expectedStage = SystemCertificationTransitionAssertions.ExpectedRevStageAfterTask(taskType);
            if (expectedStage is null)
            {
                evidence.Fail(step, $"No expected stage mapping after {taskType}.");
                return false;
            }

            if (!await SystemCertificationTransitionAssertions.AssertAfterTransitionAsync(
                    dbFactory,
                    integrity,
                    evidence,
                    step,
                    instanceId,
                    open.TaskId,
                    expectedStage,
                    cancellationToken))
            {
                return false;
            }
        }

        evidence.Fail("cert.rev.corridor", "REV happy-path loop finished without reaching CloseProject.");
        return false;
    }

    private static string Trim(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "(none)" : text.Trim();
}
