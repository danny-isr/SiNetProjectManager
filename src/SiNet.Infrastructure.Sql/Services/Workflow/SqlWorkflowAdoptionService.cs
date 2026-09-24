using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Inspection;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Thin orchestration over the existing workflow engine. Starts one instance at
/// <see cref="WorkflowAdoptionRequest.CurrentStageCode"/> and does not replay earlier stages.
/// </summary>
internal sealed class SqlWorkflowAdoptionService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    WorkflowTaskOrchestrator orchestrator,
    IProjectWorkflowPolicyService policy,
    IPilotStartGate pilotStartGate,
    ITaskQueueService taskQueue,
    IAppLogger? logger = null) : IWorkflowAdoptionService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory = dbFactory;
    private readonly WorkflowTaskOrchestrator _orchestrator = orchestrator;
    private readonly IProjectWorkflowPolicyService _policy = policy;
    private readonly IPilotStartGate _pilotStartGate = pilotStartGate;
    private readonly ITaskQueueService _taskQueue = taskQueue;
    private readonly IAppLogger? _logger = logger;

    public async ValueTask<WorkflowAdoptionOptions> GetOptionsAsync(int projectId, CancellationToken ct)
    {
        AdoptionDebugLog.Write("GetOptionsAsync", $"ENTER projectId={projectId}", _logger);
        try
        {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            .ConfigureAwait(false);
        if (project is null)
        {
            AdoptionDebugLog.Write("GetOptionsAsync", $"projectId={projectId} projectFound=false", _logger);
            return new WorkflowAdoptionOptions(
                projectId, null, [], [], "הפרויקט לא נמצא.");
        }

        var jobTypeIds = await db.TypeOfProjectInProjects.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.ProjectTypeId != null)
            .Select(t => t.ProjectTypeId!.Value)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var jobTypes = await db.JobTypes.AsNoTracking()
            .Where(j => jobTypeIds.Contains(j.Id))
            .OrderBy(j => j.Title)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var allowed = await _policy.GetAllowedWorkflowsAsync(projectId, ct).ConfigureAwait(false);
        var workflows = new List<WorkflowAdoptionWorkflowOption>();
        var rejectedByPolicy = new List<string>();
        foreach (var definition in allowed)
        {
            var stages = await db.WorkflowStageDefinitions.AsNoTracking()
                .Where(s => s.WorkflowDefinitionId == definition.Id)
                .OrderBy(s => s.SortOrder)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var jobOptions = new List<WorkflowAdoptionJobTypeOption>();
            foreach (var jobType in jobTypes)
            {
                if (!await IsWorkflowEnabledForJobTypeAsync(
                        db, projectId, jobType.Id, definition.Id, ct).ConfigureAwait(false))
                {
                    rejectedByPolicy.Add($"{definition.Code}/{jobType.Id}");
                    continue;
                }

                var stageOptions = new List<WorkflowAdoptionStageOption>();
                foreach (var stage in stages)
                {
                    if (!IsRuntimeStage(stage))
                        continue;
                    if (!await ProjectTypeWorkflowStagePolicy.IsStageAllowedAsync(
                            db, jobType.Id, definition.Id, stage.Id, ct).ConfigureAwait(false))
                        continue;

                    stageOptions.Add(new WorkflowAdoptionStageOption(
                        stage.Code ?? string.Empty,
                        stage.Name ?? stage.Code ?? string.Empty,
                        stage.SortOrder,
                        await LoadResponsibleCandidatesAsync(db, stage.AssignedGroupId, ct).ConfigureAwait(false)));
                }

                jobOptions.Add(new WorkflowAdoptionJobTypeOption(jobType.Id, jobType.Title, stageOptions));
            }

            if (jobOptions.Count == 0)
                continue;

            workflows.Add(new WorkflowAdoptionWorkflowOption(
                definition.Id, definition.Code, definition.Name, jobOptions));
        }

        var reports = await LoadReportPreviewsAsync(db, projectId, requested: null, ct).ConfigureAwait(false);
        var message = jobTypes.Count == 0
            ? "לפרויקט אין JobType. הטמעה דורשת track מפורש."
            : null;
        AdoptionDebugLog.Write(
            "GetOptionsAsync",
            $"projectId={projectId} projectFound=true jobTypeIds=[{string.Join(",", jobTypeIds)}] policyDefinitions=[{string.Join(",", allowed.Select(d => d.Code))}] returned=[{string.Join(",", workflows.Select(w => w.Code))}] rejectedPolicy=[{string.Join(",", rejectedByPolicy)}] reports={reports.Count}",
            _logger);
        return new WorkflowAdoptionOptions(project.Id, project.Title, workflows, reports, message);
        }
        catch (Exception ex)
        {
            AdoptionDebugLog.Error("GetOptionsAsync", ex, _logger);
            throw;
        }
    }

    public async ValueTask<WorkflowAdoptionPreview> PreviewAsync(
        WorkflowAdoptionRequest request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await EvaluateAsync(db, request, ct).ConfigureAwait(false);
    }

    public async ValueTask<WorkflowAdoptionCommitResult> CommitAsync(
        WorkflowAdoptionRequest request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var preview = await EvaluateAsync(db, request, ct).ConfigureAwait(false);
        if (preview.Disposition is WorkflowAdoptionDisposition.AlreadyUpToDate)
        {
            return new WorkflowAdoptionCommitResult(
                WorkflowAdoptionDisposition.AlreadyExists,
                "התהליך כבר פעיל בשלב הזה. לא נוצר מופע נוסף.",
                preview.ExistingInstanceId,
                null,
                null,
                preview.Warnings);
        }

        if (!preview.CanCommit)
        {
            return new WorkflowAdoptionCommitResult(
                preview.Disposition,
                preview.Message,
                preview.ExistingInstanceId,
                null,
                null,
                preview.Warnings);
        }

        await _pilotStartGate
            .EnsureRootStartAllowedAsync(request.UserId, request.WorkflowDefinitionId, ct)
            .ConfigureAwait(false);

        var relational = db.Database.IsRelational();
        await using var transaction = relational
            ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;

        int? startedId = null;
        WorkflowStartResultDto started;
        int? linkId = null;
        var saved = false;
        try
        {
            var notes = WorkflowAdoptionMarkers.BuildNotes(
                preview.CurrentStageCode ?? request.CurrentStageCode,
                preview.CurrentStageName,
                request.OriginalStartedAt,
                request.Notes);
            var activeReportId = request.Reports?
                .FirstOrDefault(r => r.Mode == WorkflowAdoptionReportMode.Active)
                ?.ReportId;

            started = await _orchestrator.StartWorkflowAtomicAsync(
                    request.WorkflowDefinitionId,
                    request.ProjectId,
                    WorkflowTriggerType.System,
                    triggerEntityId: null,
                    request.UserId,
                    notes,
                    ct,
                    isProjectBound: true,
                    initialStageCode: request.CurrentStageCode,
                    jobTypeId: request.JobTypeId,
                    requireCurrentStageTask: true,
                    ambientDb: db,
                    pendingInspectionReportId: activeReportId)
                .ConfigureAwait(false);
            startedId = started.Instance.Id;

            if (activeReportId is int reportId)
            {
                var taskId = await ResolveInspectionReportTaskIdAsync(db, started, ct).ConfigureAwait(false);
                linkId = await SqlInspectionReportTaskLinkService
                    .EnsureReportWorkTargetLinkOnContextAsync(db, taskId, reportId, request.UserId, ct)
                    .ConfigureAwait(false);
            }

            if (transaction is not null)
                await transaction.CommitAsync(ct).ConfigureAwait(false);

            saved = true;
        }
        catch (Exception ex) when (ex is WorkflowStartPreflightException or InvalidOperationException)
        {
            if (!relational && !saved && startedId is int orphanId)
            {
                await WorkflowTaskOrchestrator
                    .CompensateFailedAtomicStartAsync(db, orphanId, ct)
                    .ConfigureAwait(false);
            }

            return new WorkflowAdoptionCommitResult(
                WorkflowAdoptionDisposition.BlockedNotAllowed,
                ex.Message,
                null,
                null,
                null,
                preview.Warnings);
        }

        var warnings = preview.Warnings.ToList();
        var createdTaskId = started.CreatedTasks.FirstOrDefault()?.Id;
        if (request.ResponsibleUserId is int responsibleId
            && createdTaskId is int taskToAssign
            && started.CreatedTasks[0].AssignedToId != responsibleId)
        {
            try
            {
                var reassign = await _taskQueue
                    .ReassignAsync(taskToAssign, responsibleId, request.UserId, ct)
                    .ConfigureAwait(false);
                if (!reassign.Succeeded)
                {
                    warnings.Add(
                        "התהליך נוצר, אבל שיוך המשתמש האחראי נכשל. המשימה נשארה על אחראי ברירת המחדל של הקבוצה. "
                        + reassign.Message);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or WorkflowStartPreflightException or DbUpdateException)
            {
                warnings.Add(
                    "התהליך נוצר, אבל שיוך המשתמש האחראי נכשל. המשימה נשארה על אחראי ברירת המחדל של הקבוצה. "
                    + ex.Message);
            }
        }

        return new WorkflowAdoptionCommitResult(
            WorkflowAdoptionDisposition.Committed,
            "התהליך הוטמע בשלב הנוכחי.",
            started.Instance.Id,
            createdTaskId,
            linkId,
            warnings);
    }

    private async ValueTask<WorkflowAdoptionPreview> EvaluateAsync(
        SiNetSQLDbContext db,
        WorkflowAdoptionRequest request,
        CancellationToken ct)
    {
        var emptyStages = Array.Empty<WorkflowAdoptionHistoricalStage>();
        var emptyTasks = Array.Empty<WorkflowAdoptionTaskPreview>();
        var emptySkipped = Array.Empty<string>();
        var emptyActions = Array.Empty<WorkflowAdoptionSkippedAction>();
        var emptyReports = Array.Empty<WorkflowAdoptionReportPreview>();

        WorkflowAdoptionPreview Blocked(
            WorkflowAdoptionDisposition disposition,
            string message,
            int? existingId = null,
            string? existingStage = null,
            string? stageCode = null,
            string? stageName = null,
            int? sortOrder = null,
            string? nodeType = null,
            bool? isFinal = null,
            IReadOnlyList<WorkflowAdoptionHistoricalStage>? historical = null,
            IReadOnlyList<WorkflowAdoptionTaskPreview>? willCreate = null,
            IReadOnlyList<string>? willNot = null,
            IReadOnlyList<WorkflowAdoptionSkippedAction>? actions = null,
            IReadOnlyList<string>? warnings = null,
            IReadOnlyList<WorkflowAdoptionReportPreview>? reports = null,
            int? assigneeId = null,
            string? projectTitle = null,
            float? projectNumber = null,
            string? projectStatus = null,
            string? jobTitle = null,
            string? workflowCode = null,
            string? workflowName = null) =>
            new(
                disposition,
                CanCommit: false,
                message,
                request.ProjectId,
                projectTitle,
                projectNumber,
                projectStatus,
                request.JobTypeId,
                jobTitle,
                request.WorkflowDefinitionId,
                workflowCode,
                workflowName,
                stageCode,
                stageName,
                sortOrder,
                nodeType,
                isFinal,
                existingId,
                existingStage,
                historical ?? emptyStages,
                willCreate ?? emptyTasks,
                willNot ?? emptySkipped,
                actions ?? emptyActions,
                warnings ?? [],
                reports ?? emptyReports,
                assigneeId);

        if (request.ProjectId <= 0 || request.WorkflowDefinitionId <= 0 || request.JobTypeId <= 0
            || request.UserId <= 0 || string.IsNullOrWhiteSpace(request.CurrentStageCode))
        {
            return Blocked(
                WorkflowAdoptionDisposition.BlockedNotAllowed,
                "חסרים Project, Workflow, JobType, שלב או משתמש.");
        }

        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct)
            .ConfigureAwait(false);
        if (project is null)
            return Blocked(WorkflowAdoptionDisposition.BlockedNotAllowed, "הפרויקט לא נמצא.");

        string? projectStatusCode = null;
        if (project.ProjectStatusId is int statusId)
        {
            projectStatusCode = await db.ProjectStatuses.AsNoTracking()
                .Where(s => s.Id == statusId)
                .Select(s => s.Code)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        var jobType = await db.JobTypes.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobTypeId, ct)
            .ConfigureAwait(false);
        if (jobType is null)
            return Blocked(WorkflowAdoptionDisposition.BlockedNotAllowed, "JobType לא נמצא.", projectTitle: project.Title, projectNumber: project.Number, projectStatus: projectStatusCode);

        var onProject = await db.TypeOfProjectInProjects.AsNoTracking()
            .AnyAsync(t => t.ProjectId == request.ProjectId && t.ProjectTypeId == request.JobTypeId, ct)
            .ConfigureAwait(false);
        if (!onProject)
        {
            return Blocked(
                WorkflowAdoptionDisposition.BlockedNotAllowed,
                "ה־JobType אינו משויך לפרויקט.",
                projectTitle: project.Title,
                projectNumber: project.Number,
                projectStatus: projectStatusCode,
                jobTitle: jobType.Title);
        }

        if (!await IsWorkflowEnabledForJobTypeAsync(
                db, request.ProjectId, request.JobTypeId, request.WorkflowDefinitionId, ct)
                .ConfigureAwait(false))
        {
            return Blocked(
                WorkflowAdoptionDisposition.BlockedNotAllowed,
                "התהליך אינו מותר ל-JobType שנבחר.",
                projectTitle: project.Title,
                projectNumber: project.Number,
                projectStatus: projectStatusCode,
                jobTitle: jobType.Title);
        }

        var definition = await db.WorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.WorkflowDefinitionId && d.IsActive, ct)
            .ConfigureAwait(false);
        if (definition is null)
        {
            return Blocked(
                WorkflowAdoptionDisposition.BlockedNotAllowed,
                "הגדרת התהליך לא נמצאה או אינה פעילה.",
                projectTitle: project.Title,
                projectNumber: project.Number,
                projectStatus: projectStatusCode,
                jobTitle: jobType.Title);
        }

        var stages = await db.WorkflowStageDefinitions.AsNoTracking()
            .Where(s => s.WorkflowDefinitionId == definition.Id)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var stage = stages.FirstOrDefault(s =>
            string.Equals(s.Code, request.CurrentStageCode.Trim(), StringComparison.Ordinal));
        if (stage is null || string.IsNullOrWhiteSpace(stage.Code))
        {
            return Blocked(
                WorkflowAdoptionDisposition.BlockedInvalidStage,
                "השלב אינו קיים בהגדרת התהליך.",
                projectTitle: project.Title,
                projectNumber: project.Number,
                projectStatus: projectStatusCode,
                jobTitle: jobType.Title,
                workflowCode: definition.Code,
                workflowName: definition.Name);
        }

        var historical = stages
            .Where(s => s.SortOrder < stage.SortOrder && !string.IsNullOrWhiteSpace(s.Code))
            .Select(s => new WorkflowAdoptionHistoricalStage(s.Code!, s.Name, s.SortOrder))
            .ToList();
        var (willCreate, willNot, actions, expectedStatus) = await BuildEffectPreviewAsync(db, stages, stage, ct)
            .ConfigureAwait(false);
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(expectedStatus)
            && !string.Equals(projectStatusCode, expectedStatus, StringComparison.Ordinal))
        {
            warnings.Add(
                $"מצב הפרויקט הוא '{projectStatusCode ?? "ללא"}'. מעבר היסטורי שהמערכת לא מריצה היה מגדיר '{expectedStatus}'. הסטטוס לא ישתנה.");
        }

        var reports = await LoadReportPreviewsAsync(db, request.ProjectId, request.Reports, ct)
            .ConfigureAwait(false);
        foreach (var report in reports.Where(r => r.RequestedMode == WorkflowAdoptionReportMode.Historical && !r.IsLockedAfterSend && !r.HasExportSnapshot))
        {
            warnings.Add(
                $"{report.DisplayLabel} סומן כהיסטורי, אבל אין snapshot ייצוא ולכן MarkReportAsSentAsync לא ירוץ. הדוח נשאר פתוח.");
        }

        string? stageBlock = DescribeStageBlock(stage);
        if (stageBlock is null)
        {
            try
            {
                await ProjectTypeWorkflowStagePolicy.EnsureStageAllowedOrThrowAsync(
                        db, request.JobTypeId, definition.Id, stage.Id, ct)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                stageBlock = ex.Message;
            }
        }

        var existing = await ClassifyExistingAsync(db, request, stage, ct).ConfigureAwait(false);
        if (existing.Disposition is not null)
        {
            return Blocked(
                existing.Disposition.Value,
                existing.Message!,
                existing.InstanceId,
                existing.StageCode,
                stage.Code,
                stage.Name,
                stage.SortOrder,
                stage.NodeType,
                stage.IsFinal,
                historical,
                willCreate,
                willNot,
                actions,
                warnings,
                reports,
                projectTitle: project.Title,
                projectNumber: project.Number,
                projectStatus: projectStatusCode,
                jobTitle: jobType.Title,
                workflowCode: definition.Code,
                workflowName: definition.Name);
        }

        if (stageBlock is not null)
        {
            return Blocked(
                WorkflowAdoptionDisposition.BlockedInvalidStage,
                stageBlock,
                stageCode: stage.Code,
                stageName: stage.Name,
                sortOrder: stage.SortOrder,
                nodeType: stage.NodeType,
                isFinal: stage.IsFinal,
                historical: historical,
                willCreate: willCreate,
                willNot: willNot,
                actions: actions,
                warnings: warnings,
                reports: reports,
                projectTitle: project.Title,
                projectNumber: project.Number,
                projectStatus: projectStatusCode,
                jobTitle: jobType.Title,
                workflowCode: definition.Code,
                workflowName: definition.Name);
        }

        var reportBlock = DescribeReportBlock(definition.Code, request.Reports, reports);
        if (reportBlock is not null)
        {
            return Blocked(
                WorkflowAdoptionDisposition.BlockedNotAllowed,
                reportBlock,
                stageCode: stage.Code,
                stageName: stage.Name,
                sortOrder: stage.SortOrder,
                nodeType: stage.NodeType,
                isFinal: stage.IsFinal,
                historical: historical,
                willCreate: willCreate,
                willNot: willNot,
                actions: actions,
                warnings: warnings,
                reports: reports,
                projectTitle: project.Title,
                projectNumber: project.Number,
                projectStatus: projectStatusCode,
                jobTitle: jobType.Title,
                workflowCode: definition.Code,
                workflowName: definition.Name);
        }

        var reportTaskBlock = DescribeActiveReportTaskBlock(request.Reports, willCreate);
        if (reportTaskBlock is not null)
        {
            return Blocked(
                reportTaskBlock.Value.Disposition,
                reportTaskBlock.Value.Message,
                stageCode: stage.Code,
                stageName: stage.Name,
                sortOrder: stage.SortOrder,
                nodeType: stage.NodeType,
                isFinal: stage.IsFinal,
                historical: historical,
                willCreate: willCreate,
                willNot: willNot,
                actions: actions,
                warnings: warnings,
                reports: reports,
                projectTitle: project.Title,
                projectNumber: project.Number,
                projectStatus: projectStatusCode,
                jobTitle: jobType.Title,
                workflowCode: definition.Code,
                workflowName: definition.Name);
        }

        var assignee = await ResolveAssigneeAsync(db, stage, ct).ConfigureAwait(false);
        if (assignee.BlockMessage is not null)
        {
            return Blocked(
                WorkflowAdoptionDisposition.BlockedNoAssignee,
                assignee.BlockMessage,
                stageCode: stage.Code,
                stageName: stage.Name,
                sortOrder: stage.SortOrder,
                nodeType: stage.NodeType,
                isFinal: stage.IsFinal,
                historical: historical,
                willCreate: willCreate,
                willNot: willNot,
                actions: actions,
                warnings: warnings,
                reports: reports,
                projectTitle: project.Title,
                projectNumber: project.Number,
                projectStatus: projectStatusCode,
                jobTitle: jobType.Title,
                workflowCode: definition.Code,
                workflowName: definition.Name);
        }

        if (request.ResponsibleUserId is int responsibleId)
        {
            var responsibleBlock = await DescribeResponsibleBlockAsync(db, stage.AssignedGroupId, responsibleId, ct)
                .ConfigureAwait(false);
            if (responsibleBlock is not null)
            {
                return Blocked(
                    WorkflowAdoptionDisposition.BlockedResponsibleUser,
                    responsibleBlock,
                    stageCode: stage.Code,
                    stageName: stage.Name,
                    sortOrder: stage.SortOrder,
                    nodeType: stage.NodeType,
                    isFinal: stage.IsFinal,
                    historical: historical,
                    willCreate: willCreate,
                    willNot: willNot,
                    actions: actions,
                    warnings: warnings,
                    reports: reports,
                    assigneeId: assignee.UserId,
                    projectTitle: project.Title,
                    projectNumber: project.Number,
                    projectStatus: projectStatusCode,
                    jobTitle: jobType.Title,
                    workflowCode: definition.Code,
                    workflowName: definition.Name);
            }
        }

        return new WorkflowAdoptionPreview(
            WorkflowAdoptionDisposition.ReadyToAdopt,
            CanCommit: true,
            "אפשר להטמיע את התהליך בשלב שנבחר. שלבים קודמים לא ישוחזרו.",
            project.Id,
            project.Title,
            project.Number,
            projectStatusCode,
            jobType.Id,
            jobType.Title,
            definition.Id,
            definition.Code,
            definition.Name,
            stage.Code,
            stage.Name,
            stage.SortOrder,
            stage.NodeType,
            stage.IsFinal,
            null,
            null,
            historical,
            willCreate,
            willNot,
            actions,
            warnings,
            reports,
            assignee.UserId);
    }

    private static bool IsRuntimeStage(WorkflowStageDefinition stage) =>
        DescribeStageBlock(stage) is null && !string.IsNullOrWhiteSpace(stage.Code);

    private static string? DescribeStageBlock(WorkflowStageDefinition stage)
    {
        if (string.Equals(stage.NodeType, "Start", StringComparison.OrdinalIgnoreCase))
            return "שלב התחלה (Start) חסום להטמעה.";
        if (string.Equals(stage.NodeType, "SubWorkflow", StringComparison.OrdinalIgnoreCase))
            return $"שלב {stage.Name ?? stage.Code} הוא host של תת־תהליך וחסום בגרסה זו. לא נוצר child workflow.";
        if (stage.IsFinal)
            return "שלב סיום חסום להטמעה.";
        return null;
    }

    private async Task<(WorkflowAdoptionDisposition? Disposition, string? Message, int? InstanceId, string? StageCode)> ClassifyExistingAsync(
        SiNetSQLDbContext db,
        WorkflowAdoptionRequest request,
        WorkflowStageDefinition selected,
        CancellationToken ct)
    {
        var unlabeled = await db.WorkflowInstances.AsNoTracking()
            .Include(i => i.CurrentStage)
            .Where(i =>
                i.ProjectId == request.ProjectId
                && i.WorkflowDefinitionId == request.WorkflowDefinitionId
                && i.JobTypeId == null
                && i.ParentWorkflowInstanceId == null
                && i.Status != WorkflowStatus.Cancelled)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (unlabeled.Count > 0)
        {
            var one = unlabeled[0];
            return (
                WorkflowAdoptionDisposition.RequiresReviewExistingWorkflow,
                "קיים תהליך ישן לאותו פרויקט ותהליך בלי סוג עבודה. לא נוצר מופע נוסף עד הכרעה.",
                one.Id,
                one.CurrentStage?.Code);
        }

        var instances = await db.WorkflowInstances.AsNoTracking()
            .Include(i => i.CurrentStage)
            .Where(i =>
                i.ProjectId == request.ProjectId
                && i.WorkflowDefinitionId == request.WorkflowDefinitionId
                && i.JobTypeId == request.JobTypeId
                && i.ParentWorkflowInstanceId == null
                && i.Status != WorkflowStatus.Cancelled)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (instances.Any(i => i.Status == WorkflowStatus.Draft))
        {
            return (WorkflowAdoptionDisposition.BlockedConflict, "נמצא מופע במצב לא צפוי. נדרשת בדיקה.", instances[0].Id, instances[0].CurrentStage?.Code);
        }

        var live = instances.Where(i => i.Status is WorkflowStatus.Active or WorkflowStatus.Paused).ToList();
        if (live.Count > 1)
        {
            return (WorkflowAdoptionDisposition.BlockedConflict, "יותר ממופע פעיל אחד לאותו track.", live[0].Id, live[0].CurrentStage?.Code);
        }

        if (live.Count == 1)
        {
            var one = live[0];
            if (one.Status == WorkflowStatus.Paused)
            {
                return (WorkflowAdoptionDisposition.BlockedAlreadyPaused, "כבר קיים תהליך מושהה לאותו track. לא נוצר מופע נוסף.", one.Id, one.CurrentStage?.Code);
            }

            var current = one.CurrentStage;
            if (current is null)
            {
                return (WorkflowAdoptionDisposition.BlockedConflict, "לתהליך הפעיל אין שלב נוכחי.", one.Id, null);
            }

            if (string.Equals(current.Code, selected.Code, StringComparison.Ordinal))
            {
                return (WorkflowAdoptionDisposition.AlreadyUpToDate, "התהליך כבר פעיל בשלב הזה.", one.Id, current.Code);
            }

            if (current.SortOrder < selected.SortOrder)
            {
                return (WorkflowAdoptionDisposition.RequiresReviewExistingWorkflow, "כבר קיים תהליך פעיל בשלב מוקדם יותר. אין דילוג ואין שרשרת Advance.", one.Id, current.Code);
            }

            return (WorkflowAdoptionDisposition.BlockedBackwardMovement, "התהליך הפעיל נמצא בשלב מאוחר יותר. אין תנועה אחורה.", one.Id, current.Code);
        }

        var completed = instances.Where(i => i.Status == WorkflowStatus.Completed).ToList();
        if (completed.Count > 1)
        {
            return (WorkflowAdoptionDisposition.BlockedConflict, "יותר מתהליך אחד שהושלם לאותו track.", completed[0].Id, completed[0].CurrentStage?.Code);
        }

        if (completed.Count == 1)
        {
            return (WorkflowAdoptionDisposition.RequiresReviewCompletedExists, "כבר קיים תהליך שהושלם לאותו track. לא נוצר מופע חדש אוטומטית.", completed[0].Id, completed[0].CurrentStage?.Code);
        }

        return (null, null, null, null);
    }

    private static async Task<(
        IReadOnlyList<WorkflowAdoptionTaskPreview> WillCreate,
        IReadOnlyList<string> WillNot,
        IReadOnlyList<WorkflowAdoptionSkippedAction> Actions,
        string? ExpectedStatus)> BuildEffectPreviewAsync(
        SiNetSQLDbContext db,
        IReadOnlyList<WorkflowStageDefinition> stages,
        WorkflowStageDefinition selected,
        CancellationToken ct)
    {
        var earlierIds = stages.Where(s => s.SortOrder < selected.SortOrder).Select(s => s.Id).ToHashSet();
        var stageIds = stages.Select(s => s.Id).ToList();
        var templates = await db.WorkflowStageTasks.AsNoTracking()
            .Include(t => t.TaskType)
            .Where(t => stageIds.Contains(t.StageDefinitionId) && t.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var willCreate = templates
            .Where(t => t.StageDefinitionId == selected.Id)
            .OrderBy(t => t.SortOrder)
            .Select(t => new WorkflowAdoptionTaskPreview(t.TaskType?.Code, t.TaskType?.Name))
            .ToList();

        var willNot = new List<string>
        {
            "previous transitions",
            "previous-stage tasks",
            "historical transition actions",
        };
        willNot.AddRange(templates
            .Where(t => earlierIds.Contains(t.StageDefinitionId))
            .Select(t => t.TaskType?.Code)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)!);

        foreach (var host in stages.Where(s =>
                     s.SortOrder < selected.SortOrder
                     && string.Equals(s.NodeType, "SubWorkflow", StringComparison.OrdinalIgnoreCase)))
        {
            willNot.Add($"{host.Name ?? host.Code} child workflow");
        }

        var rules = await db.WorkflowTransitionRules.AsNoTracking()
            .Include(r => r.Actions)
            .Where(r => r.WorkflowDefinitionId == selected.WorkflowDefinitionId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var byId = stages.ToDictionary(s => s.Id);
        var actions = new List<WorkflowAdoptionSkippedAction>();
        string? expectedStatus = null;
        var expectedSort = int.MinValue;
        foreach (var rule in rules)
        {
            if (!byId.TryGetValue(rule.FromStageId, out var from) || !byId.TryGetValue(rule.ToStageId, out var to))
                continue;
            if (from.SortOrder >= selected.SortOrder || to.SortOrder > selected.SortOrder)
                continue;

            foreach (var action in rule.Actions.OrderBy(a => a.SortOrder))
            {
                actions.Add(new WorkflowAdoptionSkippedAction(
                    from.Code ?? from.Id.ToString(),
                    to.Code ?? to.Id.ToString(),
                    action.ActionType.ToString()));
                if (action.ActionType == WorkflowTransitionActionType.SetProjectStatus
                    && to.SortOrder >= expectedSort)
                {
                    var code = ReadProjectStatusCode(action.ConfigJson);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        expectedStatus = code;
                        expectedSort = to.SortOrder;
                    }
                }
            }
        }

        return (willCreate, willNot, actions, expectedStatus);
    }

    private static string? ReadProjectStatusCode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("ProjectStatusCode", out var value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<(int? UserId, string? BlockMessage)> ResolveAssigneeAsync(
        SiNetSQLDbContext db,
        WorkflowStageDefinition stage,
        CancellationToken ct)
    {
        var templates = await db.WorkflowStageTasks.AsNoTracking()
            .Include(t => t.TaskType)
            .Where(t => t.StageDefinitionId == stage.Id && t.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        UserGroup? group = stage.AssignedGroupId is int groupId
            ? await WorkflowStageTaskProvisioningService
                .LoadGroupWithActiveMembersAsync(db, groupId, ct)
                .ConfigureAwait(false)
            : null;
        var (groupAssignee, _) = WorkflowStageTaskProvisioningService.TryResolveAssigneeFromGroup(group);

        if (templates.Count == 0)
        {
            return groupAssignee is int id
                ? (id, null)
                : (null, "לא ניתן לפתור אחראי למשימת השלב. ההטמעה נחסמה לפני יצירה.");
        }

        int? resolved = null;
        foreach (var template in templates)
        {
            var assignee = template.DefaultAssigneeId ?? groupAssignee;
            if (assignee is null)
            {
                var label = template.TaskType?.Name ?? template.TaskType?.Code ?? "משימה";
                return (null, $"לא ניתן לפתור אחראי עבור '{label}'. ההטמעה נחסמה לפני יצירה.");
            }

            resolved ??= assignee;
        }

        return (resolved, null);
    }

    private static async Task<string?> DescribeResponsibleBlockAsync(
        SiNetSQLDbContext db,
        int? groupId,
        int userId,
        CancellationToken ct)
    {
        var user = await db.Siusers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            .ConfigureAwait(false);
        if (user is null)
            return "המשתמש האחראי אינו פעיל.";
        if (groupId is null)
            return "לשלב אין קבוצה, ולכן אי אפשר לשייך משתמש אחראי.";

        var member = await db.UserGroupMemberships.AsNoTracking()
            .AnyAsync(m => m.UserGroupId == groupId.Value && m.SiuserId == userId, ct)
            .ConfigureAwait(false);
        return member
            ? null
            : "המשתמש האחראי אינו חבר בקבוצת השלב. השיוך נחסם לפי מדיניות הקבוצה.";
    }

    private static async Task<IReadOnlyList<WorkflowAdoptionUserOption>> LoadResponsibleCandidatesAsync(
        SiNetSQLDbContext db,
        int? groupId,
        CancellationToken ct)
    {
        if (groupId is null)
            return [];

        var group = await WorkflowStageTaskProvisioningService
            .LoadGroupWithActiveMembersAsync(db, groupId.Value, ct)
            .ConfigureAwait(false);
        if (group is null)
            return [];

        return group.Memberships
            .Where(m => m.Siuser is { IsActive: true })
            .Select(m => new WorkflowAdoptionUserOption(m.Siuser!.Id, m.Siuser.Name))
            .DistinctBy(u => u.UserId)
            .OrderBy(u => u.Name)
            .ToList();
    }

    private static string? DescribeReportBlock(
        string? workflowCode,
        IReadOnlyList<WorkflowAdoptionReportIntent>? requested,
        IReadOnlyList<WorkflowAdoptionReportPreview> loaded)
    {
        if (requested is null || requested.Count == 0)
            return null;
        if (!string.Equals(workflowCode, WorkflowCodes.Review, StringComparison.Ordinal))
            return "שיוך דוחות בדיקה נתמך כרגע רק בתהליך Review.";

        if (requested.GroupBy(r => r.ReportId).Any(g => g.Count() > 1))
            return "אותו דוח מופיע יותר מפעם אחת בבקשה.";
        if (requested.Count(r => r.Mode == WorkflowAdoptionReportMode.Active) > 1)
            return "אפשר דוח פעיל אחד בלבד.";

        var known = loaded.Select(r => r.ReportId).ToHashSet();
        if (requested.Any(r => !known.Contains(r.ReportId)))
            return "אחד הדוחות אינו שייך לפרויקט.";
        return null;
    }

    private static async Task<IReadOnlyList<WorkflowAdoptionReportPreview>> LoadReportPreviewsAsync(
        SiNetSQLDbContext db,
        int projectId,
        IReadOnlyList<WorkflowAdoptionReportIntent>? requested,
        CancellationToken ct)
    {
        var reports = await db.InspectionReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.SeriesId)
            .ThenBy(r => r.ReportNumber)
            .Select(r => new
            {
                r.ReportId,
                r.ReportNumber,
                r.SeriesId,
                SeriesName = r.Series != null ? r.Series.SeriesName : null,
                r.IsLockedAfterSend,
                r.SentSpreadsheetId,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var modes = requested?.ToDictionary(r => r.ReportId, r => r.Mode)
                    ?? new Dictionary<int, WorkflowAdoptionReportMode>();
        return reports.Select(r =>
        {
            modes.TryGetValue(r.ReportId, out var mode);
            var hasMode = modes.ContainsKey(r.ReportId);
            var hasSnapshot = !string.IsNullOrWhiteSpace(r.SentSpreadsheetId);
            var note = !hasMode
                ? "לא נבחר"
                : mode == WorkflowAdoptionReportMode.Active
                    ? "פעיל — יקושר למשימת השלב הנוכחי"
                    : r.IsLockedAfterSend
                        ? "היסטורי — כבר נעול"
                        : hasSnapshot
                            ? "היסטורי — יש snapshot, אבל הנעילה לא רצה בלי MarkReportAsSent מלא"
                            : "היסטורי — נשאר פתוח. אין metadata לייצוא, ולכן אין נעילה.";
            return new WorkflowAdoptionReportPreview(
                r.ReportId,
                r.ReportNumber,
                hasMode ? mode : null,
                r.IsLockedAfterSend,
                hasSnapshot,
                note,
                r.SeriesId,
                r.SeriesName);
        }).ToList();
    }

    private async Task<bool> IsWorkflowEnabledForJobTypeAsync(
        SiNetSQLDbContext db,
        int projectId,
        int jobTypeId,
        int definitionId,
        CancellationToken ct)
    {
        var projectJobTypeIds = await db.TypeOfProjectInProjects.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.ProjectTypeId != null)
            .Select(t => t.ProjectTypeId!.Value)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var projectHasMappings = projectJobTypeIds.Count > 0
            && await db.ProjectTypeWorkflowDefinitions.AsNoTracking()
                .AnyAsync(m => projectJobTypeIds.Contains(m.ProjectTypeId), ct)
                .ConfigureAwait(false);
        if (!projectHasMappings)
            return await _policy.IsWorkflowAllowedAsync(projectId, definitionId, ct).ConfigureAwait(false);

        return await db.ProjectTypeWorkflowDefinitions.AsNoTracking()
            .AnyAsync(m =>
                m.ProjectTypeId == jobTypeId
                && m.WorkflowDefinitionId == definitionId
                && m.IsEnabled, ct)
            .ConfigureAwait(false);
    }

    private static (WorkflowAdoptionDisposition Disposition, string Message)? DescribeActiveReportTaskBlock(
        IReadOnlyList<WorkflowAdoptionReportIntent>? requested,
        IReadOnlyList<WorkflowAdoptionTaskPreview> willCreate)
    {
        if (requested is null || requested.Count(r => r.Mode == WorkflowAdoptionReportMode.Active) != 1)
            return null;

        var matches = willCreate
            .Select(t => t.TaskTypeCode)
            .Where(IsInspectionReportWorkTarget)
            .ToList();
        if (matches.Count == 1)
            return null;
        if (matches.Count == 0)
        {
            return (
                WorkflowAdoptionDisposition.BlockedNotAllowed,
                "אין בשלב הנוכחי משימה שעובדת על דוח בדיקה. דוח פעיל לא יקושר, וההטמעה נחסמה.");
        }

        return (
            WorkflowAdoptionDisposition.BlockedConflict,
            "יותר ממשימה אחת בשלב הנוכחי עובדת על דוח בדיקה. לא נבחרה משימה אוטומטית.");
    }

    internal static bool IsInspectionReportWorkTarget(string? taskTypeCode)
    {
        if (string.IsNullOrWhiteSpace(taskTypeCode))
            return false;
        var interaction = ReviewTaskInteractionRegistry.TryGet(taskTypeCode);
        return interaction?.PrimaryWorkTargetEntityType == TaskWorkTargetEntityType.InspectionReport;
    }

    private static async Task<int> ResolveInspectionReportTaskIdAsync(
        SiNetSQLDbContext db,
        WorkflowStartResultDto started,
        CancellationToken ct)
    {
        var createdIds = started.CreatedTasks.Select(t => t.Id).ToList();
        if (createdIds.Count == 0)
            throw new InvalidOperationException("אין משימת שלב לקשר אליה דוח.");

        var created = await db.ProjectAssignments.AsNoTracking()
            .Where(t => createdIds.Contains(t.Id))
            .Select(t => new { t.Id, Code = t.TaskType != null ? t.TaskType.Code : null })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var matches = created.Where(t => IsInspectionReportWorkTarget(t.Code)).ToList();
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                matches.Count == 0
                    ? "אין משימת שלב שעובדת על דוח בדיקה."
                    : "יותר ממשימה אחת בשלב עובדת על דוח בדיקה.");
        }

        return matches[0].Id;
    }
}
