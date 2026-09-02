using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Projects;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// PLN corridor for certification. Starts through production
/// <see cref="IWorkflowCommandService.StartAsync"/> (email trigger identity for ACC filing
/// links only — there is no CreatePlanning suggested action), completes FollowWorkOrder,
/// walks the hosted MaterialIntake child through FileInitialMaterials + material check,
/// and stops at open <see cref="TaskTypeCodes.OpenPlanningWorkPackage"/>.
/// </summary>
internal static class SystemCertificationPlnCorridorSupport
{
    internal sealed record Preconditions(
        int PlanningDefinitionId,
        int MaterialIntakeDefinitionId,
        int PlaceId,
        int CompanyId,
        int ContactId,
        int PlanningJobTypeId);

    internal sealed record EvidenceSteps(
        string Preconditions,
        string Project,
        string Start,
        string TransitionStart,
        string FollowWorkOrder,
        string MatChild,
        string MatCorridor,
        string MatCheck,
        string ContinuationStage,
        string ContinuationTask,
        string FinalDelta,
        string FinalAbsolute,
        SystemCertificationPrpFileMaterialProof.FilingEvidenceSteps Filing)
    {
        public static EvidenceSteps Pln { get; } = new(
            "cert.pln.preconditions",
            "cert.pln.project",
            "cert.pln.start",
            "cert.pln.transition.start",
            "cert.pln.transition.FollowWorkOrder",
            "cert.pln.mat.child",
            "cert.pln.mat.corridor",
            "cert.pln.mat.transition.CheckQuoteMaterialCompleteness",
            "cert.pln.continuation_stage",
            "cert.pln.continuation_task",
            "cert.pln.final_delta",
            "cert.pln.final_absolute",
            SystemCertificationPrpFileMaterialProof.FilingEvidenceSteps.PlnMat);

        public static EvidenceSteps Mat { get; } = new(
            "cert.mat.preconditions",
            "cert.mat.project",
            "cert.mat.start",
            "cert.mat.transition.start",
            "cert.mat.transition.FollowWorkOrder",
            "cert.mat.child",
            "cert.mat.corridor",
            "cert.mat.transition.CheckQuoteMaterialCompleteness",
            "cert.mat.continuation_stage",
            "cert.mat.continuation_task",
            "cert.mat.final_delta",
            "cert.mat.final_absolute",
            SystemCertificationPrpFileMaterialProof.FilingEvidenceSteps.Mat);
    }

    internal static async Task<Preconditions?> TryResolvePreconditionsAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationEvidence evidence,
        EvidenceSteps steps,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var planning = await db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == WorkflowCodes.PlanningWorkflow && d.IsActive, cancellationToken);
        if (planning is null)
        {
            evidence.Fail(steps.Preconditions, "Active PlanningWorkflow definition not found.");
            return null;
        }

        var material = await db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == WorkflowCodes.MaterialIntake && d.IsActive, cancellationToken);
        if (material is null)
        {
            evidence.Fail(steps.Preconditions, "Active MaterialIntake definition not found.");
            return null;
        }

        var planningJobTypeId = await db.ProjectTypeWorkflowDefinitions
            .AsNoTracking()
            .Where(m => m.WorkflowDefinitionId == planning.Id && m.IsEnabled)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.ProjectTypeId)
            .FirstOrDefaultAsync(cancellationToken);

        var place = await db.Places.AsNoTracking().FirstOrDefaultAsync(
            p => p.Title == SystemCertificationEnvironment.RequiredAccPlaceTitle,
            cancellationToken);
        if (place is null)
        {
            evidence.Fail(
                steps.Preconditions,
                $"Place '{SystemCertificationEnvironment.RequiredAccPlaceTitle}' not found on target database.");
            return null;
        }

        var company = await db.Companies.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        var contact = await db.Contacts.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);

        if (planningJobTypeId == 0 || company is null || contact is null)
        {
            evidence.Fail(
                steps.Preconditions,
                "Target database is missing planning job type mapping, company or contact rows.");
            return null;
        }

        evidence.Pass(
            steps.Preconditions,
            $"Planning definition {planning.Id}, MaterialIntake {material.Id}, "
            + $"job type {planningJobTypeId}, place {place.Id}, company {company.Id}, contact {contact.Id}.");

        return new Preconditions(
            planning.Id,
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
        EvidenceSteps steps,
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
            evidence.Fail(steps.Project, $"Cert project creation failed: {result.ErrorMessage}");
            return 0;
        }

        evidence.Pass(
            steps.Project,
            $"id={projectId} title='{result.ProjectTitle}' place='{result.PlaceTitle}' "
            + $"(JobType {pre.PlanningJobTypeId} retained for PlanningWorkflow).");
        evidence.Created("Project", projectId.ToString(), title);
        return projectId;
    }

    internal static async Task<int> StartPlanningAsync(
        IServiceProvider provider,
        Preconditions pre,
        SystemCertificationPrpCorridorSupport.CorridorInbox inbox,
        int certProjectId,
        int operatorUserId,
        SystemCertificationEvidence evidence,
        EvidenceSteps steps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(evidence);

        if (inbox.InboxMessageId is not int inboxMessageId || inboxMessageId <= 0)
        {
            evidence.Fail(
                steps.Start,
                "PLN StartAsync requires a fully ingested EmailInboxMessage so MAT FileInitialMaterials "
                + "inherits an Email work-target for ACC filing proof.");
            return 0;
        }

        var commands = provider.GetRequiredService<IWorkflowCommandService>();
        try
        {
            var start = await commands.StartAsync(
                new StartWorkflowCommand(
                    pre.PlanningDefinitionId,
                    certProjectId,
                    WorkflowTriggerTypeDto.Email,
                    inboxMessageId,
                    operatorUserId,
                    Notes: "[SYS-CERT] PLN live StartAsync",
                    IsProjectBound: true,
                    InitialStageCode: null,
                    JobTypeId: pre.PlanningJobTypeId),
                cancellationToken);

            if (start.Instance.Id <= 0)
            {
                evidence.Fail(steps.Start, "StartAsync returned a non-positive PlanningWorkflow instance id.");
                return 0;
            }

            evidence.Pass(
                steps.Start,
                $"{WorkflowCodes.PlanningWorkflow} instance id={start.Instance.Id} via IWorkflowCommandService.StartAsync "
                + $"(email trigger inbox={inboxMessageId}, jobType={pre.PlanningJobTypeId}).");
            evidence.Created("WorkflowInstance", start.Instance.Id.ToString(), WorkflowCodes.PlanningWorkflow);
            return start.Instance.Id;
        }
        catch (WorkflowStartPreflightException ex)
        {
            evidence.Fail(steps.Start, $"StartAsync refused: {Trim(ex.Message)}");
            return 0;
        }
    }

    internal static async Task<bool> WalkUntilOpenPlanningWorkPackageAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationHost.SystemCertificationRunContext context,
        SystemCertificationEvidence evidence,
        EvidenceSteps steps,
        Preconditions pre,
        int parentInstanceId,
        int certProjectId,
        int operatorUserId,
        CancellationToken cancellationToken = default)
    {
        var completion = provider.GetRequiredService<ITaskCompletionService>();

        var openAtStart = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
            dbFactory, parentInstanceId, cancellationToken);
        if (openAtStart.Count != 1
            || !string.Equals(openAtStart[0].TaskTypeCode, TaskTypeCodes.FollowWorkOrder, StringComparison.Ordinal))
        {
            evidence.Fail(
                steps.FollowWorkOrder,
                "Expected open FollowWorkOrder after PLN start; found "
                + (openAtStart.Count == 0
                    ? "none"
                    : string.Join(", ", openAtStart.Select(t => $"{t.TaskTypeCode}#{t.TaskId}"))));
            return false;
        }

        var followTaskId = openAtStart[0].TaskId;
        var followOutcome = await completion.CompleteAsync(
            new CompleteTaskCommand(
                followTaskId,
                ReviewCompletionEvents.WorkOrderReceived,
                TaskResultCodes.WorkOrderReceived,
                null,
                operatorUserId),
            cancellationToken);

        if (!followOutcome.Success || !followOutcome.TaskClosed)
        {
            evidence.Fail(
                steps.FollowWorkOrder,
                $"FollowWorkOrder #{followTaskId} completion soft/hard failed: "
                + $"Success={followOutcome.Success} TaskClosed={followOutcome.TaskClosed} "
                + $"err={Trim(followOutcome.ErrorMessage)}");
            return false;
        }

        // Parent parks on SubWorkflow host (no parent driving tasks) while MAT runs — do not
        // require a single open driving task on the parent here.
        var parentStageAfterFollow = await ReadStageCodeAsync(dbFactory, parentInstanceId, cancellationToken);
        if (!string.Equals(parentStageAfterFollow, PlanningStageCodes.ExecutionMaterialCheck, StringComparison.Ordinal)
            && !string.Equals(parentStageAfterFollow, PlanningStageCodes.PlanningStart, StringComparison.Ordinal))
        {
            evidence.Fail(
                steps.FollowWorkOrder,
                $"After FollowWorkOrder expected stage '{PlanningStageCodes.ExecutionMaterialCheck}' "
                + $"(or already '{PlanningStageCodes.PlanningStart}'); got '{parentStageAfterFollow ?? "<null>"}'.");
            return false;
        }

        evidence.Pass(
            steps.FollowWorkOrder,
            $"closed task {followTaskId}; parent stage {parentStageAfterFollow} (SubWorkflow host / advance).");

        var matInstanceId = await WaitForActiveMatChildAsync(
            dbFactory, pre.MaterialIntakeDefinitionId, parentInstanceId, evidence, steps, cancellationToken);
        if (matInstanceId <= 0)
        {
            return false;
        }

        if (!await WalkMatChildAsync(
                provider,
                dbFactory,
                integrity,
                context,
                evidence,
                steps,
                matInstanceId,
                certProjectId,
                operatorUserId,
                cancellationToken))
        {
            return false;
        }

        // Parent should now sit at PlanningStart with OpenPlanningWorkPackage.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var stage = await ReadStageCodeAsync(dbFactory, parentInstanceId, cancellationToken);
            var open = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
                dbFactory, parentInstanceId, cancellationToken);
            if (string.Equals(stage, PlanningStageCodes.PlanningStart, StringComparison.Ordinal)
                && open.Count == 1
                && string.Equals(open[0].TaskTypeCode, TaskTypeCodes.OpenPlanningWorkPackage, StringComparison.Ordinal))
            {
                evidence.Pass(
                    steps.MatCorridor,
                    $"MAT child {matInstanceId} completed; parent at {PlanningStageCodes.PlanningStart} "
                    + $"with open {TaskTypeCodes.OpenPlanningWorkPackage}#{open[0].TaskId}.");
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        var finalStage = await ReadStageCodeAsync(dbFactory, parentInstanceId, cancellationToken);
        var finalOpen = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
            dbFactory, parentInstanceId, cancellationToken);
        evidence.Fail(
            steps.MatCorridor,
            $"After MAT complete expected parent {PlanningStageCodes.PlanningStart} + open "
            + $"{TaskTypeCodes.OpenPlanningWorkPackage}; stage='{finalStage ?? "<null>"}' open=["
            + string.Join(", ", finalOpen.Select(t => $"{t.TaskTypeCode}#{t.TaskId}"))
            + "].");
        return false;
    }

    internal static async Task<bool> WalkMatChildAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationHost.SystemCertificationRunContext context,
        SystemCertificationEvidence evidence,
        EvidenceSteps steps,
        int matInstanceId,
        int certProjectId,
        int operatorUserId,
        CancellationToken cancellationToken)
    {
        var completion = provider.GetRequiredService<ITaskCompletionService>();

        for (var i = 0; i < 12; i++)
        {
            var stage = await ReadStageCodeAsync(dbFactory, matInstanceId, cancellationToken);
            if (string.Equals(stage, MaterialStageCodes.Complete, StringComparison.Ordinal))
            {
                evidence.Pass(steps.MatCorridor, $"MAT instance {matInstanceId} reached {MaterialStageCodes.Complete}.");
                return true;
            }

            var openTasks = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
                dbFactory, matInstanceId, cancellationToken);
            if (openTasks.Count == 0)
            {
                // Terminal stage may have zero tasks.
                if (string.Equals(stage, MaterialStageCodes.Complete, StringComparison.Ordinal))
                {
                    evidence.Pass(steps.MatCorridor, $"MAT instance {matInstanceId} completed with no open tasks.");
                    return true;
                }

                evidence.Fail(
                    steps.MatCorridor,
                    $"No open MAT driving tasks at step {i}, stage '{stage ?? "<null>"}'.");
                return false;
            }

            if (openTasks.Count > 1)
            {
                // AwaitingCompletion has two templates; happy path should not land there.
                evidence.Fail(
                    steps.MatCorridor,
                    $"Expected a single open MAT driving task; got "
                    + string.Join(", ", openTasks.Select(t => $"{t.TaskTypeCode}#{t.TaskId}")));
                return false;
            }

            var open = openTasks[0];
            if (string.Equals(open.TaskTypeCode, TaskTypeCodes.FileInitialMaterials, StringComparison.Ordinal))
            {
                var expectedAfter = SystemCertificationTransitionAssertions.ExpectedMatStageAfterTask(
                    open.TaskTypeCode, stage ?? string.Empty);
                if (expectedAfter is null)
                {
                    evidence.Fail(
                        steps.MatCorridor,
                        $"No MAT stage mapping for FileInitialMaterials at '{stage}'.");
                    return false;
                }

                var filed = await SystemCertificationPrpFileMaterialProof.TryProveAndCompleteAsync(
                    provider,
                    dbFactory,
                    integrity,
                    context,
                    evidence,
                    open.TaskId,
                    certProjectId,
                    matInstanceId,
                    operatorUserId,
                    cancellationToken,
                    steps.Filing,
                    expectedAfter);
                if (!filed)
                {
                    return false;
                }

                continue;
            }

            if (string.Equals(open.TaskTypeCode, TaskTypeCodes.CheckQuoteMaterialCompleteness, StringComparison.Ordinal))
            {
                var outcome = await completion.CompleteAsync(
                    new CompleteTaskCommand(
                        open.TaskId,
                        ReviewCompletionEvents.ReviewMaterialCheckCompleted,
                        TaskResultCodes.MaterialComplete,
                        null,
                        operatorUserId),
                    cancellationToken);

                if (!outcome.Success || !outcome.TaskClosed)
                {
                    evidence.Fail(
                        steps.MatCheck,
                        $"CheckQuoteMaterialCompleteness #{open.TaskId} soft/hard failed: "
                        + $"Success={outcome.Success} TaskClosed={outcome.TaskClosed} "
                        + $"err={Trim(outcome.ErrorMessage)}");
                    return false;
                }

                // Terminal MAT.Complete has no driving tasks — assert stage only.
                for (var wait = 0; wait < 20; wait++)
                {
                    var afterStage = await ReadStageCodeAsync(dbFactory, matInstanceId, cancellationToken);
                    if (string.Equals(afterStage, MaterialStageCodes.Complete, StringComparison.Ordinal))
                    {
                        evidence.Pass(
                            steps.MatCheck,
                            $"closed task {open.TaskId}; stage {MaterialStageCodes.Complete} (terminal).");
                        break;
                    }

                    await Task.Delay(250, cancellationToken);
                    if (wait == 19)
                    {
                        evidence.Fail(
                            steps.MatCheck,
                            $"After material check expected '{MaterialStageCodes.Complete}'; "
                            + $"got '{afterStage ?? "<null>"}'.");
                        return false;
                    }
                }

                continue;
            }

            evidence.Fail(
                steps.MatCorridor,
                $"Unexpected open MAT task {open.TaskId} ({open.TaskTypeCode}) at stage '{stage}'.");
            return false;
        }

        evidence.Fail(steps.MatCorridor, "Completed 12 MAT steps without reaching MAT.Complete.");
        return false;
    }

    internal static async Task<int> WaitForMatChildAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int materialDefinitionId,
        int parentInstanceId,
        SystemCertificationEvidence evidence,
        string evidenceStep,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var childId = await db.WorkflowInstances.AsNoTracking()
                .Where(w => w.ParentWorkflowInstanceId == parentInstanceId
                            && w.WorkflowDefinitionId == materialDefinitionId
                            && w.Status != WorkflowStatus.Cancelled)
                .OrderByDescending(w => w.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (childId > 0)
            {
                evidence.Pass(
                    evidenceStep,
                    $"MaterialIntake child instance id={childId} under parent {parentInstanceId}.");
                evidence.Created("WorkflowInstance", childId.ToString(), WorkflowCodes.MaterialIntake);
                return childId;
            }

            await Task.Delay(250, cancellationToken);
        }

        evidence.Fail(
            evidenceStep,
            $"No MaterialIntake child appeared under parent {parentInstanceId}.");
        return 0;
    }

    private static Task<int> WaitForActiveMatChildAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int materialDefinitionId,
        int parentInstanceId,
        SystemCertificationEvidence evidence,
        EvidenceSteps steps,
        CancellationToken cancellationToken) =>
        WaitForMatChildAsync(
            dbFactory,
            materialDefinitionId,
            parentInstanceId,
            evidence,
            steps.MatChild,
            cancellationToken);

    internal static async Task<string?> ReadStageCodeAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int instanceId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.WorkflowInstances.AsNoTracking()
            .Where(w => w.Id == instanceId)
            .Select(w => w.CurrentStage != null ? w.CurrentStage.Code : null)
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
