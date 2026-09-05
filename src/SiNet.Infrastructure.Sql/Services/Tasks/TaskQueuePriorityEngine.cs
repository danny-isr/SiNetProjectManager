using Microsoft.EntityFrameworkCore;
using SiNet.Application.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Bucket-aware work-queue priority algorithm scoped by <c>AssignedToId + WorkQueueBucket</c>.
/// Shared by native Infrastructure.Sql queue services and legacy SiNetSQL callers.
/// </summary>
public static class TaskQueuePriorityEngine
{
    private const int MaxRetries = 3;

    public static async Task<int> GetNextPriorityAsync(
        SiNetSQLDbContext context,
        int assigneeId,
        int bucket,
        CancellationToken cancellationToken = default)
    {
        ValidateBucket(bucket);

        var maxPriority = await context.ProjectAssignments
            .Where(pa => pa.AssignedToId == assigneeId
                      && pa.WorkQueueBucket == bucket
                      && pa.WorkPriority != null)
            .MaxAsync(pa => (int?)pa.WorkPriority, cancellationToken)
            .ConfigureAwait(false) ?? 0;

        return maxPriority + 1;
    }

    public static int GetNextPriority(SiNetSQLDbContext context, int assigneeId, int bucket)
    {
        ValidateBucket(bucket);

        var maxPriority = context.ProjectAssignments
            .Where(pa => pa.AssignedToId == assigneeId
                      && pa.WorkQueueBucket == bucket
                      && pa.WorkPriority != null)
            .Max(pa => (int?)pa.WorkPriority) ?? 0;

        return maxPriority + 1;
    }

    public static async Task<ProjectAssignment> InsertWithAutoPriorityAsync(
        SiNetSQLDbContext context,
        ProjectAssignment task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(task);

        if (!task.AssignedToId.HasValue)
            throw new InvalidOperationException("Cannot assign priority — AssignedToId is null.");

        WorkQueueBucketResolver.EnsureValidBucket(task, task.TaskType);

        var isActionable = await IsStatusActionableAsync(context, task.StatusId, cancellationToken)
            .ConfigureAwait(false);

        if (!isActionable)
        {
            task.WorkPriority = null;
            context.ProjectAssignments.Add(task);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return task;
        }

        // Atomic task-close + auto-advance already owns a transaction on this context.
        // Beginning a nested Serializable transaction fails SQL Server with:
        // "The connection is already in a transaction and cannot participate in another transaction."
        // (seen when provisioning FileQuoteMaterial after OpenQuoteProject completion).
        if (!SupportsSerializableTransactions(context)
            || context.Database.CurrentTransaction is not null)
            return await InsertWithAutoPrioritySimpleAsync(context, task, cancellationToken).ConfigureAwait(false);

        var attempt = 0;
        while (true)
        {
            attempt++;
            await using var transaction = await context.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var nextPriority = await GetNextPriorityAsync(
                        context,
                        task.AssignedToId.Value,
                        task.WorkQueueBucket,
                        cancellationToken)
                    .ConfigureAwait(false);

                task.WorkPriority = nextPriority;
                context.ProjectAssignments.Add(task);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return task;
            }
            catch (Exception ex) when (IsTransientOrDeadlock(ex) && attempt < MaxRetries)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                context.Entry(task).State = EntityState.Detached;
                await Task.Delay(50 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static ProjectAssignment InsertWithAutoPriority(SiNetSQLDbContext context, ProjectAssignment task)
        => Task.Run(() => InsertWithAutoPriorityAsync(context, task, CancellationToken.None))
            .GetAwaiter()
            .GetResult();

    private static async Task<ProjectAssignment> InsertWithAutoPrioritySimpleAsync(
        SiNetSQLDbContext context,
        ProjectAssignment task,
        CancellationToken cancellationToken)
    {
        task.WorkPriority = await GetNextPriorityAsync(
                context,
                task.AssignedToId!.Value,
                task.WorkQueueBucket,
                cancellationToken)
            .ConfigureAwait(false);
        context.ProjectAssignments.Add(task);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return task;
    }

    private static bool SupportsSerializableTransactions(SiNetSQLDbContext context)
        => context.Database.IsRelational()
           && context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    public static void CompactAfterRemoval(
        SiNetSQLDbContext context,
        int employeeId,
        int bucket,
        int removedPriority,
        bool saveChanges = true)
    {
        ValidateBucket(bucket);

        var tasksToShift = context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.WorkPriority > removedPriority)
            .ToList();

        foreach (var task in tasksToShift)
            task.WorkPriority--;

        if (saveChanges && tasksToShift.Count > 0)
            context.SaveChanges();
    }

    public static async Task CompactAfterRemovalAsync(
        SiNetSQLDbContext context,
        int employeeId,
        int bucket,
        int removedPriority,
        CancellationToken cancellationToken = default,
        bool saveChanges = true)
    {
        ValidateBucket(bucket);

        var tasksToShift = await context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.WorkPriority > removedPriority)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var task in tasksToShift)
            task.WorkPriority--;

        if (saveChanges && tasksToShift.Count > 0)
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public static int ValidateAndReindex(SiNetSQLDbContext context, int employeeId, int bucket)
    {
        ValidateBucket(bucket);

        var shellIds = GetCollisionShellParentIds(context, employeeId, bucket);

        var openTasks = context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.AssignmentStatus != null
                     && t.AssignmentStatus.IsActionable
                     && !shellIds.Contains(t.Id))
            .OrderBy(t => t.WorkPriority.HasValue ? 0 : 1)
            .ThenBy(t => t.WorkPriority)
            .ThenBy(t => t.Created)
            .ToList();

        var staleClosed = context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.WorkPriority != null
                     && t.AssignmentStatus != null
                     && !t.AssignmentStatus.IsActionable)
            .ToList();

        var corrected = 0;

        foreach (var stale in staleClosed)
        {
            stale.WorkPriority = null;
            corrected++;
        }

        for (var i = 0; i < openTasks.Count; i++)
        {
            var expected = i + 1;
            if (openTasks[i].WorkPriority != expected)
            {
                openTasks[i].WorkPriority = expected;
                corrected++;
            }
        }

        if (corrected > 0)
            context.SaveChanges();

        return corrected;
    }

    public static async Task<int> ValidateAndReindexAsync(
        SiNetSQLDbContext context,
        int employeeId,
        int bucket,
        CancellationToken cancellationToken = default)
    {
        var detail = await ValidateAndReindexDetailedAsync(context, employeeId, bucket, cancellationToken)
            .ConfigureAwait(false);
        return detail.TotalCorrected;
    }

    /// <summary>
    /// Analyses queue defects, reindexes actionable queue members to 1..N, clears stale priorities on closed tasks.
    /// Collision shells (null <c>WorkPriority</c>, parent with children) stay non-queued.
    /// </summary>
    public static async Task<TaskQueueRepairDetail> ValidateAndReindexDetailedAsync(
        SiNetSQLDbContext context,
        int employeeId,
        int bucket,
        CancellationToken cancellationToken = default)
    {
        ValidateBucket(bucket);

        var shellIds = await GetCollisionShellParentIdsAsync(context, employeeId, bucket, cancellationToken)
            .ConfigureAwait(false);

        var openTasks = await context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.AssignmentStatus != null
                     && t.AssignmentStatus.IsActionable
                     && !shellIds.Contains(t.Id))
            .OrderBy(t => t.WorkPriority.HasValue ? 0 : 1)
            .ThenBy(t => t.WorkPriority)
            .ThenBy(t => t.Created)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var staleClosed = await context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.WorkPriority != null
                     && t.AssignmentStatus != null
                     && !t.AssignmentStatus.IsActionable)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var nullPriorities = openTasks.Count(t => !t.WorkPriority.HasValue);
        var duplicatePriorities = openTasks
            .Where(t => t.WorkPriority.HasValue)
            .GroupBy(t => t.WorkPriority!.Value)
            .Where(g => g.Count() > 1)
            .Sum(g => g.Count() - 1);

        var gapsClosed = CountPriorityGaps(openTasks);

        var staleClosedCleared = 0;
        foreach (var stale in staleClosed)
        {
            stale.WorkPriority = null;
            staleClosedCleared++;
        }

        var assignedFromNull = 0;
        var renumbered = 0;
        for (var i = 0; i < openTasks.Count; i++)
        {
            var expected = i + 1;
            var task = openTasks[i];
            if (!task.WorkPriority.HasValue)
                assignedFromNull++;

            if (task.WorkPriority != expected)
            {
                task.WorkPriority = expected;
                renumbered++;
            }
        }

        if (staleClosedCleared > 0 || renumbered > 0)
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new TaskQueueRepairDetail(
            NullPrioritiesFixed: nullPriorities,
            DuplicatePrioritiesFixed: duplicatePriorities,
            GapsClosed: gapsClosed,
            StaleClosedCleared: staleClosedCleared,
            TotalCorrected: staleClosedCleared + renumbered);
    }

    /// <summary>
    /// Collision-shell parents: <c>ParentAssignmentId == null</c>, <c>WorkPriority == null</c>, with at least one child.
    /// These remain open/actionable for hierarchy but are not queue members.
    /// </summary>
    private static HashSet<int> GetCollisionShellParentIds(
        SiNetSQLDbContext context,
        int employeeId,
        int bucket)
    {
        return context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.WorkPriority == null
                     && t.ParentAssignmentId == null
                     && context.ProjectAssignments.Any(c => c.ParentAssignmentId == t.Id))
            .Select(t => t.Id)
            .ToHashSet();
    }

    private static async Task<HashSet<int>> GetCollisionShellParentIdsAsync(
        SiNetSQLDbContext context,
        int employeeId,
        int bucket,
        CancellationToken cancellationToken)
    {
        var ids = await context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.WorkPriority == null
                     && t.ParentAssignmentId == null
                     && context.ProjectAssignments.Any(c => c.ParentAssignmentId == t.Id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ids.ToHashSet();
    }

    public static async Task<TaskQueueRepairDetail> ValidateAndReindexAllDetailedAsync(
        SiNetSQLDbContext context,
        CancellationToken cancellationToken = default)
    {
        var pairs = await context.ProjectAssignments
            .Where(t => t.AssignedToId != null
                     && t.AssignmentStatus != null
                     && t.AssignmentStatus.IsActionable)
            .Select(t => new { EmployeeId = t.AssignedToId!.Value, t.WorkQueueBucket })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var aggregate = new TaskQueueRepairDetail(0, 0, 0, 0, 0);
        foreach (var pair in pairs)
        {
            var detail = await ValidateAndReindexDetailedAsync(
                    context, pair.EmployeeId, pair.WorkQueueBucket, cancellationToken)
                .ConfigureAwait(false);
            aggregate = MergeDetails(aggregate, detail);
        }

        return aggregate;
    }

    private static TaskQueueRepairDetail MergeDetails(TaskQueueRepairDetail left, TaskQueueRepairDetail right) =>
        new(
            left.NullPrioritiesFixed + right.NullPrioritiesFixed,
            left.DuplicatePrioritiesFixed + right.DuplicatePrioritiesFixed,
            left.GapsClosed + right.GapsClosed,
            left.StaleClosedCleared + right.StaleClosedCleared,
            left.TotalCorrected + right.TotalCorrected);

    private static int CountPriorityGaps(IReadOnlyList<ProjectAssignment> openTasks)
    {
        var priorities = openTasks
            .Where(t => t.WorkPriority.HasValue)
            .Select(t => t.WorkPriority!.Value)
            .OrderBy(p => p)
            .ToList();

        if (priorities.Count == 0)
            return openTasks.Count > 0 ? openTasks.Count : 0;

        var gaps = 0;
        if (priorities[0] != 1)
            gaps++;

        for (var i = 1; i < priorities.Count; i++)
        {
            if (priorities[i] != priorities[i - 1] + 1)
                gaps++;
        }

        if (priorities.Count != priorities[^1])
            gaps++;

        return gaps;
    }

    public static int ValidateAndReindexAll(SiNetSQLDbContext context)
    {
        var pairs = context.ProjectAssignments
            .Where(t => t.AssignedToId != null
                     && t.AssignmentStatus != null
                     && t.AssignmentStatus.IsActionable)
            .Select(t => new { EmployeeId = t.AssignedToId!.Value, t.WorkQueueBucket })
            .Distinct()
            .ToList();

        var totalCorrected = 0;
        foreach (var pair in pairs)
            totalCorrected += ValidateAndReindex(context, pair.EmployeeId, pair.WorkQueueBucket);

        return totalCorrected;
    }

    /// <summary>
    /// Clears <see cref="ProjectAssignment.WorkPriority"/> and optionally shifts higher positions
    /// down by 1 within the same assignee+bucket. No-op when the task was not a queue member
    /// (<c>WorkPriority</c> null — e.g. collision shells).
    /// </summary>
    /// <param name="saveChanges">
    /// When <c>false</c>, only mutates tracked entities; the caller owns <c>SaveChanges</c> /
    /// the ambient transaction (required for atomic task-close + workflow advance).
    /// </param>
    public static void RemoveFromQueue(
        SiNetSQLDbContext context,
        ProjectAssignment task,
        bool compact = true,
        bool saveChanges = true)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!task.AssignedToId.HasValue || !task.WorkPriority.HasValue)
            return;

        var employeeId = task.AssignedToId.Value;
        var bucket = WorkQueueBucketResolver.Resolve(task);
        var removedPriority = task.WorkPriority.Value;

        task.WorkPriority = null;
        if (saveChanges)
            context.SaveChanges();

        if (compact)
            CompactAfterRemoval(context, employeeId, bucket, removedPriority, saveChanges);
    }

    /// <inheritdoc cref="RemoveFromQueue(SiNetSQLDbContext, ProjectAssignment, bool, bool)"/>
    public static async Task RemoveFromQueueAsync(
        SiNetSQLDbContext context,
        ProjectAssignment task,
        bool compact = true,
        bool saveChanges = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!task.AssignedToId.HasValue || !task.WorkPriority.HasValue)
            return;

        var employeeId = task.AssignedToId.Value;
        var bucket = WorkQueueBucketResolver.Resolve(task);
        var removedPriority = task.WorkPriority.Value;

        task.WorkPriority = null;
        if (saveChanges)
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (compact)
        {
            await CompactAfterRemovalAsync(
                    context,
                    employeeId,
                    bucket,
                    removedPriority,
                    cancellationToken,
                    saveChanges)
                .ConfigureAwait(false);
        }
    }

    public static void AppendToQueueEnd(SiNetSQLDbContext context, ProjectAssignment task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!task.AssignedToId.HasValue)
            throw new InvalidOperationException("Cannot append task without AssignedToId.");

        WorkQueueBucketResolver.EnsureValidBucket(task, task.TaskType);
        task.WorkPriority = GetNextPriority(context, task.AssignedToId.Value, task.WorkQueueBucket);
        context.SaveChanges();
    }

    private static async Task<bool> IsStatusActionableAsync(
        SiNetSQLDbContext context,
        int? statusId,
        CancellationToken cancellationToken)
    {
        if (statusId == null)
            return true;

        return await context.ProjectAssignmentStatuses
            .Where(s => s.Id == statusId.Value)
            .Select(s => s.IsActionable)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ValidateBucket(int bucket)
    {
        if (!WorkQueueBucketCodes.IsValid(bucket))
            throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "Invalid work queue bucket.");
    }

    private static bool IsTransientOrDeadlock(Exception ex)
    {
        var current = ex;
        while (current != null)
        {
            if (current is Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                foreach (Microsoft.Data.SqlClient.SqlError err in sqlEx.Errors)
                {
                    if (err.Number is 1205 or 41302 or 41305)
                        return true;
                }
            }

            current = current.InnerException;
        }

        return false;
    }
}
