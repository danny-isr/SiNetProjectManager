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

        if (!SupportsSerializableTransactions(context))
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
        int removedPriority)
    {
        ValidateBucket(bucket);

        var tasksToShift = context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.WorkPriority > removedPriority)
            .ToList();

        foreach (var task in tasksToShift)
            task.WorkPriority--;

        if (tasksToShift.Count > 0)
            context.SaveChanges();
    }

    public static async Task CompactAfterRemovalAsync(
        SiNetSQLDbContext context,
        int employeeId,
        int bucket,
        int removedPriority,
        CancellationToken cancellationToken = default)
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

        if (tasksToShift.Count > 0)
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public static int ValidateAndReindex(SiNetSQLDbContext context, int employeeId, int bucket)
    {
        ValidateBucket(bucket);

        var openTasks = context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.AssignmentStatus != null
                     && t.AssignmentStatus.IsActionable)
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
        ValidateBucket(bucket);

        var openTasks = await context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.AssignmentStatus != null
                     && t.AssignmentStatus.IsActionable)
            .OrderBy(t => t.WorkPriority.HasValue ? 0 : 1)
            .ThenBy(t => t.WorkPriority)
            .ThenBy(t => t.Created)
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
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return corrected;
    }

    /// <summary>
    /// Analyses queue defects, reindexes actionable tasks to 1..N, clears stale priorities on closed tasks.
    /// </summary>
    public static async Task<TaskQueueRepairDetail> ValidateAndReindexDetailedAsync(
        SiNetSQLDbContext context,
        int employeeId,
        int bucket,
        CancellationToken cancellationToken = default)
    {
        ValidateBucket(bucket);

        var openTasks = await context.ProjectAssignments
            .Where(t => t.AssignedToId == employeeId
                     && t.WorkQueueBucket == bucket
                     && t.AssignmentStatus != null
                     && t.AssignmentStatus.IsActionable)
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

    public static void RemoveFromQueue(
        SiNetSQLDbContext context,
        ProjectAssignment task,
        bool compact = true)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!task.AssignedToId.HasValue || !task.WorkPriority.HasValue)
            return;

        var employeeId = task.AssignedToId.Value;
        var bucket = WorkQueueBucketResolver.Resolve(task);
        var removedPriority = task.WorkPriority.Value;

        task.WorkPriority = null;
        context.SaveChanges();

        if (compact)
            CompactAfterRemoval(context, employeeId, bucket, removedPriority);
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
