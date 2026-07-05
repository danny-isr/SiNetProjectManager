using Microsoft.EntityFrameworkCore;
using SiNet.Application.DevTools;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>
/// Idempotent demo tasks for Task Panel read-only (DEBUG development DB only).
/// Each open demo task uses a dedicated <see cref="TaskType.Code"/> so
/// <c>IX_ProjectAssignment_UniqueOpenTask</c> is not violated.
/// </summary>
public sealed class SqlTaskDemoSeedService
{
    public const string TitlePrefix = "DEBUG_TASK_SEED";
    public const string DemoProjectName = "DEBUG — פרויקט בדיקת משימות";
    public const string DemoTaskTypeCodePrefix = "DEBUG_TASK_SEED_";

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlTaskDemoSeedService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    /// <summary>Canonical demo task definitions — one unique TaskType per open row with WorkPriority.</summary>
    internal static IReadOnlyList<DemoTaskSpec> DemoTaskCatalog { get; } =
    [
        new("DEBUG_TASK_SEED_QUICK_1",   $"{TitlePrefix} Quick 1",   WorkQueueBucketCodes.Quick,  1, true,  "דמו — קצר 1",   901),
        new("DEBUG_TASK_SEED_QUICK_2",   $"{TitlePrefix} Quick 2",   WorkQueueBucketCodes.Quick,  2, true,  "דמו — קצר 2",   902),
        new("DEBUG_TASK_SEED_QUICK_3",   $"{TitlePrefix} Quick 3",   WorkQueueBucketCodes.Quick,  3, true,  "דמו — קצר 3",   903),
        new("DEBUG_TASK_SEED_MEDIUM_1",  $"{TitlePrefix} Medium 1",  WorkQueueBucketCodes.Medium, 1, true,  "דמו — בינוני 1", 911),
        new("DEBUG_TASK_SEED_MEDIUM_2",  $"{TitlePrefix} Medium 2",  WorkQueueBucketCodes.Medium, 2, true,  "דמו — בינוני 2", 912),
        new("DEBUG_TASK_SEED_MEDIUM_3",  $"{TitlePrefix} Medium 3",  WorkQueueBucketCodes.Medium, 3, true,  "דמו — בינוני 3", 913),
        new("DEBUG_TASK_SEED_LONG_1",    $"{TitlePrefix} Long 1",    WorkQueueBucketCodes.Long,   1, true,  "דמו — ארוך 1",  921),
        new("DEBUG_TASK_SEED_LONG_2",    $"{TitlePrefix} Long 2",    WorkQueueBucketCodes.Long,   2, true,  "דמו — ארוך 2",  922),
        new("DEBUG_TASK_SEED_LONG_3",    $"{TitlePrefix} Long 3",    WorkQueueBucketCodes.Long,   3, true,  "דמו — ארוך 3",  923),
        new("DEBUG_TASK_SEED_CLOSED",    $"{TitlePrefix} Closed (non-open)", WorkQueueBucketCodes.Medium, null, false, "דמו — סגור", 930),
        new("DEBUG_TASK_SEED_NO_PRIORITY", $"{TitlePrefix} Medium no-priority", WorkQueueBucketCodes.Medium, null, true, "דמו — ללא priority", 931),
    ];

    /// <summary>Resolve preview task — reuses production TaskType; only one open row with WorkPriority.</summary>
    internal const string ResolveCandidateTitle = $"{TitlePrefix} Resolve candidate (Email filing)";

    public async Task<SeedResult> SeedAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

            var user = await db.Siusers
                .Where(u => u.IsActive)
                .OrderBy(u => u.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (user is null)
                return Fail("No active user found in database — demo tasks require an existing Siuser.");

            var project = await FindOrCreateDemoProjectAsync(db, ct).ConfigureAwait(false);
            var openStatus = await db.ProjectAssignmentStatuses
                .FirstOrDefaultAsync(s => s.Code == TaskStatusCodes.Open && s.IsOpen && s.IsActionable, ct)
                .ConfigureAwait(false)
                ?? await db.ProjectAssignmentStatuses.FirstOrDefaultAsync(s => s.IsOpen && s.IsActionable, ct)
                    .ConfigureAwait(false);

            if (openStatus is null)
                return Fail("No open/actionable ProjectAssignmentStatus found — run static seed first.");

            var closedStatus = await db.ProjectAssignmentStatuses
                .FirstOrDefaultAsync(s => s.Code == TaskStatusCodes.Completed, ct)
                .ConfigureAwait(false)
                ?? await db.ProjectAssignmentStatuses.FirstOrDefaultAsync(s => !s.IsOpen, ct)
                    .ConfigureAwait(false);

            var filingType = await db.TaskTypes
                .FirstOrDefaultAsync(t => t.Code == TaskTypeCodes.FileInitialInquiry, ct)
                .ConfigureAwait(false);

            await EnsureDemoTaskTypesAsync(db, ct).ConfigureAwait(false);

            var taskTypesByCode = await db.TaskTypes
                .Where(t => t.Code.StartsWith(DemoTaskTypeCodePrefix))
                .ToDictionaryAsync(t => t.Code, StringComparer.OrdinalIgnoreCase, ct)
                .ConfigureAwait(false);

            var existingTitles = await db.ProjectAssignments
                .Where(t => t.Title != null && t.Title.StartsWith(TitlePrefix))
                .Select(t => t.Title!)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var existingSet = existingTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;
            var pending = new List<ProjectAssignment>();

            foreach (var spec in DemoTaskCatalog)
            {
                if (!taskTypesByCode.TryGetValue(spec.TaskTypeCode, out var taskType))
                    return Fail($"Demo TaskType '{spec.TaskTypeCode}' missing after ensure — run static seed first.");

                if (existingSet.Contains(spec.Title))
                    continue;

                var statusId = spec.RequiresOpenStatus ? openStatus.Id : closedStatus?.Id ?? openStatus.Id;
                pending.Add(CreateTask(spec, project.Id, user.Id, statusId, taskType.Id, now));
                existingSet.Add(spec.Title);
            }

            if (filingType is not null && !existingSet.Contains(ResolveCandidateTitle))
            {
                pending.Add(CreateTask(
                    ResolveCandidateTitle,
                    WorkQueueBucketCodes.Quick,
                    1,
                    project.Id,
                    user.Id,
                    openStatus.Id,
                    filingType.Id,
                    now));
            }

            if (pending.Count > 0)
            {
                db.ProjectAssignments.AddRange(pending);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return new SeedResult
            {
                Succeeded = true,
                Summary = pending.Count == 0
                    ? "Demo tasks already present (idempotent — no new rows)."
                    : $"Created {pending.Count} demo task(s) for user {user.Id} on project {project.Id}.",
            };
        }
        catch (DbUpdateException ex) when (IsUniqueOpenTaskViolation(ex))
        {
            DevToolsLog.Error(ex, "[DemoSeed] IX_ProjectAssignment_UniqueOpenTask violation");
            return Fail(
                "Demo seed failed: duplicate open task identity (IX_ProjectAssignment_UniqueOpenTask). " +
                "Each open demo task must use a unique TaskType for the same project/user.");
        }
        catch (Exception ex)
        {
            DevToolsLog.Error(ex, "[DemoSeed] Unexpected failure");
            return Fail(ex.Message);
        }
    }

    internal static async Task EnsureDemoTaskTypesAsync(SiNetSQLDbContext db, CancellationToken ct)
    {
        var existingCodes = await db.TaskTypes
            .Where(t => t.Code.StartsWith(DemoTaskTypeCodePrefix))
            .Select(t => t.Code)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, ct)
            .ConfigureAwait(false);

        var inserted = false;
        foreach (var spec in DemoTaskCatalog)
        {
            if (existingCodes.Contains(spec.TaskTypeCode))
                continue;

            db.TaskTypes.Add(new TaskType
            {
                Code = spec.TaskTypeCode,
                Name = spec.TaskTypeName,
                IsActive = true,
                SortOrder = spec.SortOrder,
                DefaultWorkQueueBucket = spec.Bucket,
            });
            existingCodes.Add(spec.TaskTypeCode);
            inserted = true;
        }

        if (inserted)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static ProjectAssignment CreateTask(
        DemoTaskSpec spec,
        int projectId,
        int assignedToId,
        int statusId,
        int taskTypeId,
        DateTime now) =>
        CreateTask(spec.Title, spec.Bucket, spec.WorkPriority, projectId, assignedToId, statusId, taskTypeId, now);

    private static ProjectAssignment CreateTask(
        string title,
        int bucket,
        int? workPriority,
        int projectId,
        int assignedToId,
        int statusId,
        int taskTypeId,
        DateTime now)
    {
        return new ProjectAssignment
        {
            Title = title,
            ProjectId = projectId,
            AssignedToId = assignedToId,
            StatusId = statusId,
            TaskTypeId = taskTypeId,
            WorkQueueBucket = bucket,
            WorkPriority = workPriority,
            Created = now,
            Modified = now,
        };
    }

    private async Task<Project> FindOrCreateDemoProjectAsync(SiNetSQLDbContext db, CancellationToken ct)
    {
        var existing = await db.Projects
            .FirstOrDefaultAsync(p => p.Title == DemoProjectName, ct)
            .ConfigureAwait(false);

        if (existing is not null)
            return existing;

        var any = await db.Projects.OrderBy(p => p.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (any is not null)
            return any;

        var project = new Project
        {
            Title = DemoProjectName,
            Created = DateTime.UtcNow,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return project;
    }

    private static bool IsUniqueOpenTaskViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_ProjectAssignment_UniqueOpenTask", StringComparison.OrdinalIgnoreCase);
    }

    private static SeedResult Fail(string message) =>
        new() { Succeeded = false, Summary = message, Errors = [message] };

    internal sealed record DemoTaskSpec(
        string TaskTypeCode,
        string Title,
        int Bucket,
        int? WorkPriority,
        bool RequiresOpenStatus,
        string TaskTypeName,
        int SortOrder);
}
