using System.IO;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Workbench queue lists must not show collision-shell parents (WorkPriority=null) as blank-position cards.
/// </summary>
public sealed class TaskWorkbenchQueueMembershipTests
{
    [Fact]
    public async Task Shell_parent_with_null_WorkPriority_is_not_shown_in_Workbench_queue()
    {
        var shell = Sample(277, WorkQueueBucketCodes.Medium, workPriority: null, title: "תיוק חומר ראשוני — תהליך #82");
        var child = Sample(290, WorkQueueBucketCodes.Medium, workPriority: 35, title: "תיוק חומר ראשוני");
        var vm = CreateVm(shell, child);

        await vm.LoadAsync();

        Assert.DoesNotContain(vm.MediumTasks, t => t.TaskId == 277);
        Assert.Contains(vm.MediumTasks, t => t.TaskId == 290);
    }

    [Fact]
    public async Task Queued_child_is_shown_and_priorities_remain_ordered()
    {
        var tasks = new[]
        {
            Sample(281, WorkQueueBucketCodes.Medium, 33, "פתיחת בדיקה חדשה"),
            Sample(288, WorkQueueBucketCodes.Medium, 34, "מעקב אישור הצעה"),
            Sample(290, WorkQueueBucketCodes.Medium, 35, "תיוק חומר ראשוני"),
            Sample(277, WorkQueueBucketCodes.Medium, null, "תיוק חומר ראשוני — תהליך #82"),
            Sample(280, WorkQueueBucketCodes.Medium, null, "פתיחת בדיקה חדשה — תהליך #84"),
            Sample(289, WorkQueueBucketCodes.Medium, null, "תיוק חומר ראשוני — תהליך #85"),
        };
        var vm = CreateVm(tasks);

        await vm.LoadAsync();

        Assert.Equal(new[] { 281, 288, 290 }, vm.MediumTasks.Select(t => t.TaskId).ToArray());
        Assert.Equal(new int?[] { 33, 34, 35 }, vm.MediumTasks.Select(t => t.WorkPriority).ToArray());
    }

    [Fact]
    public async Task MyTasks_scope_excludes_shells()
    {
        var vm = CreateVm(
            Sample(288, WorkQueueBucketCodes.Medium, 34, "queued"),
            Sample(289, WorkQueueBucketCodes.Medium, null, "shell — תהליך #85"));
        await vm.LoadAsync();

        Assert.Equal(TaskWorkbenchScope.MyTasks, vm.SelectedScope);
        Assert.Single(vm.MediumTasks);
        Assert.Equal(288, vm.MediumTasks[0].TaskId);
    }

    [Fact]
    public async Task SpecificUser_scope_excludes_shells()
    {
        var vm = CreateVm(
            admin: true,
            Sample(281, WorkQueueBucketCodes.Medium, 33, "queued"),
            Sample(280, WorkQueueBucketCodes.Medium, null, "shell — תהליך #84"));
        await vm.LoadAsync();
        vm.SelectedScope = TaskWorkbenchScope.SpecificUser;
        vm.SelectedUserId = 14;
        await vm.LoadAsync();

        Assert.DoesNotContain(vm.MediumTasks, t => t.TaskId == 280);
        Assert.Contains(vm.MediumTasks, t => t.TaskId == 281);
    }

    [Fact]
    public async Task AllUsers_scope_excludes_shells()
    {
        var vm = CreateVm(
            admin: true,
            Sample(290, WorkQueueBucketCodes.Medium, 35, "queued"),
            Sample(277, WorkQueueBucketCodes.Medium, null, "shell — תהליך #82"));
        await vm.LoadAsync();
        vm.SelectedScope = TaskWorkbenchScope.AllUsers;
        await vm.LoadAsync();

        Assert.DoesNotContain(vm.MediumTasks, t => t.TaskId == 277);
        Assert.Contains(vm.MediumTasks, t => t.TaskId == 290);
    }

    [Fact]
    public async Task Project_filter_does_not_reintroduce_shell_rows()
    {
        var queued = Sample(290, WorkQueueBucketCodes.Medium, 35, "queued", projectId: 1041);
        var shell = Sample(289, WorkQueueBucketCodes.Medium, null, "shell — תהליך #85", projectId: 1041);
        var other = Sample(288, WorkQueueBucketCodes.Medium, 34, "other project", projectId: 1042);
        var vm = CreateVm(queued, shell, other);

        await vm.LocalProjectFilterSelector!.InitializeAsync();
        var project = vm.LocalProjectFilterSelector.Projects.First(p => p.ProjectId == 1041);
        vm.LocalProjectFilterSelector.SelectProjectCommand.Execute(project);
        await vm.LoadAsync();

        Assert.True(vm.FilterTasksByProjectEnabled);
        Assert.Single(vm.MediumTasks);
        Assert.Equal(290, vm.MediumTasks[0].TaskId);
        Assert.DoesNotContain(vm.MediumTasks, t => t.TaskId == 289);
    }

    [Fact]
    public void Sql_bucket_queries_require_non_null_WorkPriority()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "SiNet.Infrastructure.Sql",
            "Services",
            "Tasks",
            "SqlTaskQueryService.cs"));

        Assert.Contains("t.WorkPriority != null", sql, StringComparison.Ordinal);
        Assert.Contains("shell parents stay WorkPriority=null", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Parent_child_shell_relationship_unchanged_in_provisioning()
    {
        var provisioning = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "SiNet.Infrastructure.Sql",
            "Services",
            "Workflow",
            "WorkflowStageTaskProvisioningService.cs"));

        Assert.Contains("WorkPriority = null", provisioning, StringComparison.Ordinal);
        Assert.Contains("creating child under shell", provisioning, StringComparison.Ordinal);
    }

    private static TaskWorkbenchViewModel CreateVm(params TaskSummaryDto[] tasks) =>
        CreateVm(admin: false, tasks);

    private static TaskWorkbenchViewModel CreateVm(bool admin, params TaskSummaryDto[] tasks)
    {
        var query = new BucketQuery(tasks);
        return new TaskWorkbenchViewModel(
            query,
            new StubNav(),
            null,
            new StubUser(14),
            new StubAuthorization(admin),
            new StubUserLookup([new UserLookupDto(14, "User 14", true)]),
            null,
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            null);
    }

    private static TaskSummaryDto Sample(
        int id,
        int bucket,
        int? workPriority,
        string title,
        int projectId = 1041) =>
        new(
            TaskId: id,
            ProjectId: projectId,
            TaskTypeCode: "T",
            TaskTypeName: "Type",
            StatusCode: "Open",
            StatusName: "Open",
            IsOpen: true,
            AssignedToUserId: 14,
            AssignedToUserName: "User 14",
            WorkQueueBucket: bucket,
            WorkQueueBucketCode: WorkQueueBucketCodes.ToCode(bucket),
            WorkQueueBucketDisplayName: WorkQueueBucketCodes.ToDisplayName(bucket),
            WorkPriority: workPriority,
            DueDate: null,
            CreatedAt: DateTime.UtcNow,
            LastTaskResultCode: null,
            Title: title,
            ComponentKey: null);

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class BucketQuery(IReadOnlyList<TaskSummaryDto> all) : ITaskQueryService
    {
        public ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult(all.FirstOrDefault(t => t.TaskId == taskId));

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(
            int projectId, bool includeClosed = false, int? workQueueBucket = null, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>(
                all.Where(t => t.ProjectId == projectId).ToList());

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(
            int userId, int? workQueueBucket = null, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>(
                all.Where(t => t.AssignedToUserId == userId
                               && (workQueueBucket is null || t.WorkQueueBucket == workQueueBucket)).ToList());

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserByBucketAsync(
            int userId, int workQueueBucket, CancellationToken ct) =>
            // Intentionally returns shells too — Workbench boundary must still filter them.
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>(
                all.Where(t => t.AssignedToUserId == userId && t.WorkQueueBucket == workQueueBucket)
                    .OrderBy(t => t.WorkPriority ?? int.MaxValue)
                    .ToList());

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(
            int workQueueBucket, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>(
                all.Where(t => t.WorkQueueBucket == workQueueBucket)
                    .OrderBy(t => t.WorkPriority ?? int.MaxValue)
                    .ToList());
    }

    private sealed class StubNav : ITaskNavigationService
    {
        public ValueTask<Application.WorkSurfaces.WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult<Application.WorkSurfaces.WorkSurfaceContext?>(null);
    }

    private sealed class StubUser(int id) : ICurrentUserContext
    {
        public int? UserId { get; } = id;
    }

    private sealed class StubAuthorization(bool admin) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default) =>
            Task.FromResult(admin && requiredRole <= AppRole.Administrator);

        public Task<bool> CanCurrentUserAccessFeatureAsync(string featureCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(admin && featureCode == AppFeatureCodes.TaskWorkbenchViewOtherUsersTasks);
    }

    private sealed class StubUserLookup(IReadOnlyList<UserLookupDto> users) : IUserLookupService
    {
        public Task<IReadOnlyList<UserLookupDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(users);
    }
}
