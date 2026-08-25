using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Shared transition assertions every certification scenario reuses after completing a driving task.
/// </summary>
internal static class SystemCertificationTransitionAssertions
{
    internal sealed record OpenDrivingTask(
        int TaskId,
        string TaskTypeCode,
        int? AssignedToId);

    internal sealed record InstanceStage(int InstanceId, string? StageCode, WorkflowStatus Status);

    /// <summary>
    /// After a transition: old task closed, current stage matches, exactly one open driving task,
    /// assignee is active, delta integrity is clean.
    /// </summary>
    public static async Task AssertAfterTransitionAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationEvidence evidence,
        string step,
        int instanceId,
        int closedTaskId,
        string expectedStageCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(integrity);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedStageCode);

        var failures = new List<string>();
        var details = new List<string>();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var closed = await db.ProjectAssignments
            .AsNoTracking()
            .Include(t => t.AssignmentStatus)
            .FirstOrDefaultAsync(t => t.Id == closedTaskId, cancellationToken);

        if (closed is null)
        {
            failures.Add($"task {closedTaskId} missing");
        }
        else if (closed.AssignmentStatus?.IsOpen == true)
        {
            failures.Add($"task {closedTaskId} still open");
        }
        else
        {
            details.Add($"closed task {closedTaskId}");
        }

        var stage = await ReadInstanceStageAsync(db, instanceId, cancellationToken);
        if (stage.StageCode is null)
        {
            failures.Add("instance has no current stage");
        }
        else if (!string.Equals(stage.StageCode, expectedStageCode, StringComparison.Ordinal))
        {
            failures.Add($"stage '{stage.StageCode}' != expected '{expectedStageCode}'");
        }
        else
        {
            details.Add($"stage {expectedStageCode}");
        }

        var openTasks = await FindOpenDrivingTasksAsync(db, instanceId, cancellationToken);
        if (openTasks.Count != 1)
        {
            failures.Add($"{openTasks.Count} open driving tasks, expected 1");
        }
        else
        {
            var open = openTasks[0];
            details.Add($"open task {open.TaskId} ({open.TaskTypeCode})");
            await CollectAssigneeFailureAsync(db, open, failures, cancellationToken);
        }

        var report = await integrity.CheckAsync(cancellationToken);
        if (!report.IsDeltaClean)
        {
            failures.Add(report.DescribeDelta());
        }
        else
        {
            details.Add("delta clean");
        }

        Record(evidence, step, failures, details);
    }

    /// <summary>
    /// Verifies the expected open state immediately after a workflow start (no prior task to close).
    /// </summary>
    public static async Task AssertOpenStateAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationIntegrityValidator integrity,
        SystemCertificationEvidence evidence,
        string step,
        int instanceId,
        string expectedStageCode,
        string expectedOpenTaskTypeCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(integrity);
        ArgumentNullException.ThrowIfNull(evidence);

        var failures = new List<string>();
        var details = new List<string>();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var stage = await ReadInstanceStageAsync(db, instanceId, cancellationToken);
        if (!string.Equals(stage.StageCode, expectedStageCode, StringComparison.Ordinal))
        {
            failures.Add(
                $"stage '{stage.StageCode ?? "<null>"}' != expected '{expectedStageCode}'");
        }
        else
        {
            details.Add($"stage {expectedStageCode}");
        }

        var openTasks = await FindOpenDrivingTasksAsync(db, instanceId, cancellationToken);
        if (openTasks.Count != 1
            || !string.Equals(openTasks[0].TaskTypeCode, expectedOpenTaskTypeCode, StringComparison.Ordinal))
        {
            failures.Add(
                "expected one open "
                + expectedOpenTaskTypeCode
                + ", found "
                + (openTasks.Count == 0
                    ? "none"
                    : string.Join(", ", openTasks.Select(t => t.TaskTypeCode))));
        }
        else
        {
            details.Add($"open task {openTasks[0].TaskId} ({expectedOpenTaskTypeCode})");
            await CollectAssigneeFailureAsync(db, openTasks[0], failures, cancellationToken);
        }

        var report = await integrity.CheckAsync(cancellationToken);
        if (!report.IsDeltaClean)
        {
            failures.Add(report.DescribeDelta());
        }
        else
        {
            details.Add("delta clean");
        }

        Record(evidence, step, failures, details);
    }

    /// <summary>
    /// Verifies the expected continuation state at a policy boundary (for example SendQuote blocked).
    /// </summary>
    public static async Task AssertContinuationStateAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        SystemCertificationEvidence evidence,
        string stageStep,
        string taskStep,
        int instanceId,
        string expectedStageCode,
        string expectedOpenTaskTypeCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(evidence);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var stage = await ReadInstanceStageAsync(db, instanceId, cancellationToken);
        if (!string.Equals(stage.StageCode, expectedStageCode, StringComparison.Ordinal))
        {
            evidence.Fail(
                stageStep,
                $"Instance {instanceId} is on '{stage.StageCode ?? "<null>"}', expected '{expectedStageCode}'.");
        }
        else
        {
            evidence.Pass(stageStep, $"Instance {instanceId} waits at {expectedStageCode}.");
        }

        var openTasks = await FindOpenDrivingTasksAsync(db, instanceId, cancellationToken);
        if (openTasks.Count != 1
            || !string.Equals(openTasks[0].TaskTypeCode, expectedOpenTaskTypeCode, StringComparison.Ordinal))
        {
            evidence.Fail(
                taskStep,
                $"Expected open task {expectedOpenTaskTypeCode}, found "
                + (openTasks.Count == 0
                    ? "none"
                    : string.Join(", ", openTasks.Select(t => t.TaskTypeCode))));
        }
        else
        {
            evidence.Pass(taskStep, $"Open task {openTasks[0].TaskId} is {expectedOpenTaskTypeCode}.");
        }
    }

    private static async Task CollectAssigneeFailureAsync(
        SiNetSQLDbContext db,
        OpenDrivingTask open,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        if (open.AssignedToId is not int userId)
        {
            failures.Add($"task {open.TaskId} has no assignee");
            return;
        }

        var active = await db.Siusers
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        if (!active)
        {
            failures.Add($"task {open.TaskId} assigned to inactive user {userId}");
        }
    }

    private static void Record(
        SystemCertificationEvidence evidence,
        string step,
        List<string> failures,
        List<string> details)
    {
        if (failures.Count > 0)
        {
            evidence.Fail(step, string.Join("; ", failures));
            return;
        }

        evidence.Pass(step, string.Join("; ", details));
    }

    /// <summary>Loads stage/status for an instance; returns null when the row is missing.</summary>
    internal static async Task<InstanceStage?> LoadInstanceStageAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int instanceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        return instance is null
            ? null
            : new InstanceStage(instanceId, instance.CurrentStage?.Code, instance.Status);
    }

    private static async Task<InstanceStage> ReadInstanceStageAsync(
        SiNetSQLDbContext db,
        int instanceId,
        CancellationToken cancellationToken)
    {
        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.CurrentStage)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        return instance is null
            ? new InstanceStage(instanceId, null, WorkflowStatus.Cancelled)
            : new InstanceStage(instanceId, instance.CurrentStage?.Code, instance.Status);
    }

    internal static async Task<List<OpenDrivingTask>> FindOpenDrivingTasksAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int instanceId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await FindOpenDrivingTasksAsync(db, instanceId, cancellationToken);
    }

    private static async Task<List<OpenDrivingTask>> FindOpenDrivingTasksAsync(
        SiNetSQLDbContext db,
        int instanceId,
        CancellationToken cancellationToken)
    {
        return await db.ProjectAssignments
            .AsNoTracking()
            .Include(t => t.TaskType)
            .Include(t => t.AssignmentStatus)
            .Where(t => t.AssignmentStatus!.IsOpen
                     && t.TaskLinks.Any(l =>
                            l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                         && l.LinkedEntityId == instanceId
                         && l.Role == TaskLinkRole.Trigger))
            .OrderBy(t => t.Id)
            .Select(t => new OpenDrivingTask(t.Id, t.TaskType!.Code, t.AssignedToId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Expected PRP stage after completing a driving task through the happy path.</summary>
    internal static string? ExpectedStageAfterTask(string completedTaskTypeCode) =>
        completedTaskTypeCode switch
        {
            TaskTypeCodes.OpenQuoteProject => ProposalStageCodes.FileMaterial,
            TaskTypeCodes.FileQuoteMaterial => ProposalStageCodes.MaterialCheck,
            TaskTypeCodes.CheckQuoteMaterialCompleteness => ProposalStageCodes.Calculation,
            TaskTypeCodes.PrepareQuoteCalculation => ProposalStageCodes.Preparation,
            TaskTypeCodes.PrepareQuoteDocument => ProposalStageCodes.InternalApproval,
            TaskTypeCodes.ApproveQuoteInternal => ProposalStageCodes.SendQuote,
            _ => null,
        };

    internal static IReadOnlyList<string> PrpHappyPathTaskTypes { get; } =
    [
        TaskTypeCodes.OpenQuoteProject,
        TaskTypeCodes.FileQuoteMaterial,
        TaskTypeCodes.CheckQuoteMaterialCompleteness,
        TaskTypeCodes.PrepareQuoteCalculation,
        TaskTypeCodes.PrepareQuoteDocument,
        TaskTypeCodes.ApproveQuoteInternal,
    ];
}
