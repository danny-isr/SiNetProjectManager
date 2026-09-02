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
/// OUT corridor for certification. Starts through production
/// <see cref="IWorkflowCommandService.StartAsync"/> on a disposable [SYS-CERT] project and attempts
/// to complete each OUT.* driving task through <see cref="ITaskCompletionService"/> only.
/// </summary>
internal static class SystemCertificationOutCorridorSupport
{
    internal sealed record Preconditions(
        int OutsourcingDefinitionId,
        int PlaceId,
        int CompanyId,
        int ContactId,
        int PlanningJobTypeId);

    internal static async Task<Preconditions?> TryResolvePreconditionsAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var outsourcing = await db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Code == WorkflowCodes.Outsourcing && d.IsActive, cancellationToken);
        if (outsourcing is null)
        {
            evidence.Fail("cert.out.preconditions", "Active Outsourcing workflow definition not found.");
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
                "cert.out.preconditions",
                $"Active {WorkflowCodes.PlanningWorkflow} definition not found "
                + "(needed only to pick a JobType for disposable project create).");
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
                "cert.out.preconditions",
                $"Place '{SystemCertificationEnvironment.RequiredAccPlaceTitle}' not found on target database.");
            return null;
        }

        var company = await db.Companies.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        var contact = await db.Contacts.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);

        if (planningJobTypeId == 0 || company is null || contact is null)
        {
            evidence.Fail(
                "cert.out.preconditions",
                "Target database is missing planning job type mapping, company or contact rows.");
            return null;
        }

        evidence.Pass(
            "cert.out.preconditions",
            $"Outsourcing definition {outsourcing.Id}, planning job type {planningJobTypeId}, "
            + $"place {place.Id}, company {company.Id}, contact {contact.Id}.");

        return new Preconditions(
            outsourcing.Id,
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
        var title = $"{SystemCertificationEnvironment.CertificationTitlePrefix} OUT {DateTime.Now:MMdd-HHmm}";

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
            evidence.Fail("cert.out.project", $"Cert project creation failed: {result.ErrorMessage}");
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
            "cert.out.project",
            $"id={projectId} title='{result.ProjectTitle}' (JobTypes cleared — OUT open-policy start).");
        evidence.Created("Project", projectId.ToString(), title);
        return projectId;
    }

    internal static async Task<int> StartOutsourcingAsync(
        IServiceProvider provider,
        Preconditions pre,
        int certProjectId,
        int operatorUserId,
        SystemCertificationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        var commands = provider.GetRequiredService<IWorkflowCommandService>();
        try
        {
            var start = await commands.StartAsync(
                new StartWorkflowCommand(
                    pre.OutsourcingDefinitionId,
                    certProjectId,
                    WorkflowTriggerTypeDto.Manual,
                    TriggerEntityId: null,
                    operatorUserId,
                    Notes: "[SYS-CERT] OUT live StartAsync",
                    IsProjectBound: true),
                cancellationToken);

            if (start.Instance.Id <= 0)
            {
                evidence.Fail("cert.out.start", "StartAsync returned a non-positive Outsourcing instance id.");
                return 0;
            }

            evidence.Pass(
                "cert.out.start",
                $"{WorkflowCodes.Outsourcing} instance id={start.Instance.Id} via IWorkflowCommandService.StartAsync "
                + $"(manual trigger, project={certProjectId}).");
            evidence.Created("WorkflowInstance", start.Instance.Id.ToString(), WorkflowCodes.Outsourcing);
            return start.Instance.Id;
        }
        catch (WorkflowStartPreflightException ex)
        {
            evidence.Fail("cert.out.start", $"StartAsync refused: {Trim(ex.Message)}");
            return 0;
        }
    }

    internal static async Task<bool> WalkContractAAsync(
        IServiceProvider provider,
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationEvidence evidence,
        int instanceId,
        int operatorUserId,
        CancellationToken cancellationToken = default)
    {
        var completion = provider.GetRequiredService<ITaskCompletionService>();
        var metadata = provider.GetRequiredService<ITaskCompletionMetadataResolver>();

        foreach (var taskType in SystemCertificationTransitionAssertions.OutHappyPathTaskTypes)
        {
            var openTasks = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
                dbFactory, instanceId, cancellationToken);
            if (openTasks.Count != 1
                || !string.Equals(openTasks[0].TaskTypeCode, taskType, StringComparison.Ordinal))
            {
                evidence.Fail(
                    $"cert.out.transition.{taskType}",
                    $"Expected one open {taskType}; found "
                    + (openTasks.Count == 0
                        ? "none"
                        : string.Join(", ", openTasks.Select(t => $"{t.TaskTypeCode}#{t.TaskId}"))));
                return false;
            }

            var open = openTasks[0];
            var step = $"cert.out.transition.{taskType}";
            var eventCode = metadata.ResolveCompletionEventCode(taskType, taskResultCode: null);
            if (string.IsNullOrWhiteSpace(eventCode))
            {
                evidence.Fail(
                    step,
                    "PRODUCT CONTRACT GAP — ITaskCompletionService has no completion event for "
                    + $"{taskType}. OUT transitions rely on AllRequiredTasksClosed + AllTasksComplete "
                    + "without TaskResultEquals, but production cannot close the task without a "
                    + "ReviewCompletionEventBehavior + ReviewTaskInteractionRegistry seam.");
                return false;
            }

            var outcome = await completion.CompleteAsync(
                new CompleteTaskCommand(
                    open.TaskId,
                    eventCode,
                    TaskResultCode: null,
                    CompletedTaskLinkIds: null,
                    operatorUserId),
                cancellationToken);

            if (!outcome.Success || !outcome.TaskClosed)
            {
                evidence.Fail(
                    step,
                    $"PRODUCT BUG — {taskType} #{open.TaskId} completion refused despite resolved "
                    + $"event '{eventCode}': Success={outcome.Success} TaskClosed={outcome.TaskClosed} "
                    + $"err={Trim(outcome.ErrorMessage)}");
                return false;
            }

            var expectedStage = SystemCertificationTransitionAssertions.ExpectedOutStageAfterTask(taskType);
            if (expectedStage is null)
            {
                evidence.Fail(step, $"No expected stage mapping after {taskType}.");
                return false;
            }

            if (string.Equals(expectedStage, OutsourcingStageCodes.Complete, StringComparison.Ordinal))
            {
                for (var wait = 0; wait < 40; wait++)
                {
                    if (await TryIsTerminalCompletedAsync(dbFactory, instanceId, cancellationToken))
                    {
                        var report = await integrity.CheckAsync(cancellationToken);
                        if (!report.IsDeltaClean)
                        {
                            evidence.Fail(step, report.DescribeDelta());
                            return false;
                        }

                        evidence.Pass(
                            step,
                            $"closed task {open.TaskId}; stage {OutsourcingStageCodes.Complete}; "
                            + "workflow Completed; zero open tasks; delta clean.");
                        if (!await AssertTerminalCompletedAsync(dbFactory, evidence, instanceId, cancellationToken))
                        {
                            return false;
                        }

                        return true;
                    }

                    await Task.Delay(250, cancellationToken);
                }

                evidence.Fail(
                    step,
                    $"After {taskType} expected terminal {OutsourcingStageCodes.Complete} with zero open tasks.");
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

        evidence.Fail("cert.out.corridor", "OUT loop finished without reaching terminal Complete.");
        return false;
    }

    private static async Task<bool> TryIsTerminalCompletedAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int instanceId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var instance = await db.WorkflowInstances.AsNoTracking()
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);
        if (instance is null || instance.Status != WorkflowStatus.Completed)
        {
            return false;
        }

        if (!string.Equals(instance.CurrentStage?.Code, OutsourcingStageCodes.Complete, StringComparison.Ordinal))
        {
            return false;
        }

        var openTasks = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
            dbFactory, instanceId, cancellationToken);
        return openTasks.Count == 0;
    }

    internal static async Task<bool> AssertTerminalCompletedAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationEvidence evidence,
        int instanceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var instance = await db.WorkflowInstances.AsNoTracking()
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);
        if (instance is null)
        {
            evidence.Fail("cert.out.terminal", $"Outsourcing instance {instanceId} missing.");
            return false;
        }

        if (instance.Status != WorkflowStatus.Completed)
        {
            evidence.Fail(
                "cert.out.terminal",
                $"Outsourcing instance {instanceId} status={instance.Status}, expected Completed.");
            return false;
        }

        if (!string.Equals(instance.CurrentStage?.Code, OutsourcingStageCodes.Complete, StringComparison.Ordinal))
        {
            evidence.Fail(
                "cert.out.terminal",
                $"Instance stage='{instance.CurrentStage?.Code ?? "<null>"}', expected {OutsourcingStageCodes.Complete}.");
            return false;
        }

        var openTasks = await SystemCertificationTransitionAssertions.FindOpenDrivingTasksAsync(
            dbFactory, instanceId, cancellationToken);
        if (openTasks.Count > 0)
        {
            evidence.Fail(
                "cert.out.terminal",
                $"Outsourcing instance has {openTasks.Count} open driving task(s).");
            return false;
        }

        var danglingLinks = await (
            from tl in db.TaskLinks.AsNoTracking()
            join pa in db.ProjectAssignments.AsNoTracking() on tl.TaskId equals pa.Id
            join st in db.ProjectAssignmentStatuses.AsNoTracking() on pa.StatusId equals st.Id
            where tl.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                  && tl.LinkedEntityId == instanceId
                  && tl.Role == TaskLinkRole.Trigger
                  && st.IsOpen
            select tl.Id).CountAsync(cancellationToken);
        if (danglingLinks > 0)
        {
            evidence.Fail(
                "cert.out.terminal",
                $"Found {danglingLinks} TaskLink(s) on non-closed trigger tasks for instance {instanceId}.");
            return false;
        }

        evidence.Pass(
            "cert.out.terminal",
            $"Outsourcing instance {instanceId} Completed at {OutsourcingStageCodes.Complete}; "
            + "zero open driving tasks; no dangling trigger TaskLinks.");
        return true;
    }

    private static string Trim(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "(none)" : text.Trim();
}
