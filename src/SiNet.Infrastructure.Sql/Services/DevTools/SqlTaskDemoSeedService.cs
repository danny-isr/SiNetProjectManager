using Microsoft.EntityFrameworkCore;
using SiNet.Application.DevTools;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>
/// Idempotent demo tasks for Task Panel read-only (DEBUG development DB only).
/// </summary>
public sealed class SqlTaskDemoSeedService
{
    public const string TitlePrefix = "DEBUG_TASK_SEED";
    public const string DemoProjectName = "DEBUG — פרויקט בדיקת משימות";

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlTaskDemoSeedService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async Task<SeedResult> SeedAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var user = await db.Siusers
            .Where(u => u.IsActive)
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Fail("No active user found in database — demo tasks require an existing Siuser.");
        }

        var project = await FindOrCreateDemoProjectAsync(db, ct).ConfigureAwait(false);
        var openStatus = await db.ProjectAssignmentStatuses
            .FirstOrDefaultAsync(s => s.Code == TaskStatusCodes.Open && s.IsOpen && s.IsActionable, ct)
            .ConfigureAwait(false)
            ?? await db.ProjectAssignmentStatuses.FirstOrDefaultAsync(s => s.IsOpen && s.IsActionable, ct)
                .ConfigureAwait(false);

        if (openStatus is null)
        {
            return Fail("No open/actionable ProjectAssignmentStatus found — run static seed first.");
        }

        var closedStatus = await db.ProjectAssignmentStatuses
            .FirstOrDefaultAsync(s => s.Code == TaskStatusCodes.Completed, ct)
            .ConfigureAwait(false)
            ?? await db.ProjectAssignmentStatuses.FirstOrDefaultAsync(s => !s.IsOpen, ct)
                .ConfigureAwait(false);

        var generalType = await db.TaskTypes.FirstOrDefaultAsync(t => t.Code == "General", ct)
            .ConfigureAwait(false);
        var filingType = await db.TaskTypes.FirstOrDefaultAsync(t => t.Code == TaskTypeCodes.FileInitialInquiry, ct)
            .ConfigureAwait(false);

        if (generalType is null)
        {
            return Fail("TaskType 'General' not found — run static seed first.");
        }

        var existing = await db.ProjectAssignments
            .Where(t => t.Title != null && t.Title.StartsWith(TitlePrefix))
            .Select(t => t.Title!)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var created = 0;
        var pending = new List<ProjectAssignment>();

        void Queue(string title, int bucket, int? workPriority, int statusId, int taskTypeId)
        {
            if (existingSet.Contains(title))
                return;

            pending.Add(CreateTask(title, bucket, workPriority, project.Id, user.Id, statusId, taskTypeId, now));
            existingSet.Add(title);
            created++;
        }

        Queue($"{TitlePrefix} Quick 1", WorkQueueBucketCodes.Quick, 1, openStatus.Id, generalType.Id);
        Queue($"{TitlePrefix} Quick 2", WorkQueueBucketCodes.Quick, 2, openStatus.Id, generalType.Id);
        Queue($"{TitlePrefix} Quick 3", WorkQueueBucketCodes.Quick, 3, openStatus.Id, generalType.Id);
        Queue($"{TitlePrefix} Medium 1", WorkQueueBucketCodes.Medium, 1, openStatus.Id, generalType.Id);
        Queue($"{TitlePrefix} Medium 2", WorkQueueBucketCodes.Medium, 2, openStatus.Id, generalType.Id);
        Queue($"{TitlePrefix} Medium 3", WorkQueueBucketCodes.Medium, 3, openStatus.Id, generalType.Id);
        Queue($"{TitlePrefix} Long 1", WorkQueueBucketCodes.Long, 1, openStatus.Id, generalType.Id);
        Queue($"{TitlePrefix} Long 2", WorkQueueBucketCodes.Long, 2, openStatus.Id, generalType.Id);
        Queue($"{TitlePrefix} Long 3", WorkQueueBucketCodes.Long, 3, openStatus.Id, generalType.Id);

        if (closedStatus is not null)
        {
            Queue($"{TitlePrefix} Closed (non-open)", WorkQueueBucketCodes.Medium, null, closedStatus.Id, generalType.Id);
        }

        Queue($"{TitlePrefix} Medium no-priority", WorkQueueBucketCodes.Medium, null, openStatus.Id, generalType.Id);

        if (filingType is not null)
        {
            Queue($"{TitlePrefix} Resolve candidate (Email filing)", WorkQueueBucketCodes.Quick, 1, openStatus.Id, filingType.Id);
        }

        if (pending.Count > 0)
        {
            db.ProjectAssignments.AddRange(pending);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new SeedResult
        {
            Succeeded = true,
            Summary = created == 0
                ? "Demo tasks already present (idempotent — no new rows)."
                : $"Created {created} demo task(s) for user {user.Id} on project {project.Id}.",
        };
    }

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

    private static SeedResult Fail(string message) =>
        new() { Succeeded = false, Summary = message, Errors = [message] };
}
