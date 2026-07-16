using System.IO;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.App.Wpf.Surfaces.Tasks;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class TaskWorkbenchScopeTests
{
    [Fact]
    public async Task Non_admin_scope_is_my_tasks_only()
    {
        var vm = CreateViewModel(admin: false, userId: 10);

        await vm.LoadAsync();

        Assert.False(vm.CanSelectTaskScope);
        Assert.Single(vm.AvailableScopes);
        Assert.Equal(TaskWorkbenchScope.MyTasks, vm.AvailableScopes[0].Scope);
        Assert.Equal(TaskWorkbenchScope.MyTasks, vm.SelectedScope);
        Assert.Equal("MyTasks", vm.LoadMode);
    }

    [Fact]
    public async Task Non_admin_cannot_select_specific_user_or_all_users()
    {
        var vm = CreateViewModel(admin: false, userId: 10);
        await vm.LoadAsync();

        vm.SelectedScope = TaskWorkbenchScope.AllUsers;

        Assert.Equal(TaskWorkbenchScope.MyTasks, vm.SelectedScope);
        Assert.Contains("אין הרשאה", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_scope_options_include_my_specific_all()
    {
        var vm = CreateViewModel(admin: true, userId: 10);
        await vm.LoadAsync();

        Assert.True(vm.CanSelectTaskScope);
        Assert.Equal(3, vm.AvailableScopes.Count);
        Assert.Contains(vm.AvailableScopes, o => o.Scope == TaskWorkbenchScope.MyTasks);
        Assert.Contains(vm.AvailableScopes, o => o.Scope == TaskWorkbenchScope.SpecificUser);
        Assert.Contains(vm.AvailableScopes, o => o.Scope == TaskWorkbenchScope.AllUsers);
    }

    [Fact]
    public async Task Admin_default_scope_is_my_tasks()
    {
        var vm = CreateViewModel(admin: true, userId: 10);
        await vm.LoadAsync();

        Assert.Equal(TaskWorkbenchScope.MyTasks, vm.SelectedScope);
        Assert.Equal("MyTasks", vm.LoadMode);
    }

    [Fact]
    public async Task Admin_specific_user_scope_loads_selected_user_tasks()
    {
        var query = new RecordingScopeQueryService();
        var vm = new TaskWorkbenchViewModel(
            query,
            new StubNav(),
            new StubWorkbench(),
            new StubUser(10),
            new StubAuthorization(admin: true),
            new StubUserLookup([new UserLookupDto(12, "User 12", true), new UserLookupDto(99, "User 99", true)]));

        await vm.LoadAsync();
        var callsBefore = query.UserBucketCalls;
        vm.SelectedScope = TaskWorkbenchScope.SpecificUser;
        vm.SelectedUserId = 12;

        Assert.Equal(12, query.LastUserId);
        Assert.Equal("SpecificUser", vm.LoadMode);
        Assert.True(query.UserBucketCalls > callsBefore);
    }

    [Fact]
    public async Task Admin_all_users_scope_loads_all_open_tasks()
    {
        var query = new RecordingScopeQueryService
        {
            AllUsersQuick = [CreateTask(1, 12, WorkQueueBucketCodes.Quick)],
            AllUsersMedium = [CreateTask(2, 99, WorkQueueBucketCodes.Medium)],
        };

        var vm = new TaskWorkbenchViewModel(
            query,
            new StubNav(),
            null,
            new StubUser(10),
            new StubAuthorization(admin: true),
            new StubUserLookup([new UserLookupDto(12, "U12", true)]));

        await vm.LoadAsync();
        vm.SelectedScope = TaskWorkbenchScope.AllUsers;

        Assert.Equal(3, query.AllUsersBucketCalls);
        Assert.Equal("AllUsers", vm.LoadMode);
        Assert.Single(vm.QuickTasks);
        Assert.Single(vm.MediumTasks);
    }

    [Fact]
    public async Task Changing_scope_reloads_tasks()
    {
        var query = new RecordingScopeQueryService();
        var vm = CreateViewModel(admin: true, userId: 10, query: query);

        await vm.LoadAsync();
        var initialCalls = query.UserBucketCalls + query.AllUsersBucketCalls;

        vm.SelectedScope = TaskWorkbenchScope.AllUsers;
        await vm.LoadAsync();

        Assert.True(query.AllUsersBucketCalls >= 3);
        Assert.True(query.UserBucketCalls + query.AllUsersBucketCalls > initialCalls);
    }

    [Fact]
    public async Task Changing_selected_user_reloads_specific_user_tasks()
    {
        var query = new RecordingScopeQueryService();
        var vm = CreateViewModel(admin: true, userId: 10, query: query);

        await vm.LoadAsync();
        vm.SelectedScope = TaskWorkbenchScope.SpecificUser;
        vm.SelectedUserId = 12;
        await vm.LoadAsync();

        var callsAfter12 = query.UserBucketCalls;
        vm.SelectedUserId = 99;
        await vm.LoadAsync();

        Assert.Equal(99, query.LastUserId);
        Assert.True(query.UserBucketCalls > callsAfter12);
    }

    [Fact]
    public async Task Specific_user_scope_requires_selected_user()
    {
        var query = new RecordingScopeQueryService();
        var vm = CreateViewModel(admin: true, userId: 10, query: query);

        await vm.LoadAsync();
        vm.SelectedScope = TaskWorkbenchScope.SpecificUser;
        var callsBefore = query.UserBucketCalls;
        await vm.LoadAsync();

        Assert.Contains("בחר משתמש", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(vm.QuickTasks);
        Assert.Equal(callsBefore, query.UserBucketCalls);
    }

    [Fact]
    public async Task All_users_scope_groups_by_bucket()
    {
        var query = new RecordingScopeQueryService
        {
            AllUsersQuick = [CreateTask(1, 12, WorkQueueBucketCodes.Quick)],
            AllUsersMedium = [CreateTask(2, 12, WorkQueueBucketCodes.Medium)],
            AllUsersLong = [CreateTask(3, 99, WorkQueueBucketCodes.Long)],
        };

        var vm = CreateViewModel(admin: true, userId: 10, query: query);
        await vm.LoadAsync();
        vm.SelectedScope = TaskWorkbenchScope.AllUsers;
        await vm.LoadAsync();

        Assert.Single(vm.QuickTasks);
        Assert.Single(vm.MediumTasks);
        Assert.Single(vm.LongTasks);
        Assert.Equal(WorkQueueBucketCodes.Quick, vm.QuickTasks[0].WorkQueueBucket);
        Assert.Equal(WorkQueueBucketCodes.Medium, vm.MediumTasks[0].WorkQueueBucket);
        Assert.Equal(WorkQueueBucketCodes.Long, vm.LongTasks[0].WorkQueueBucket);
    }

    [Fact]
    public async Task All_users_scope_displays_assigned_to()
    {
        var query = new RecordingScopeQueryService
        {
            AllUsersQuick = [CreateTask(1, 12, WorkQueueBucketCodes.Quick, "Alice")],
        };

        var vm = CreateViewModel(admin: true, userId: 10, query: query);
        await vm.LoadAsync();
        vm.SelectedScope = TaskWorkbenchScope.AllUsers;
        await vm.LoadAsync();

        Assert.Equal("Alice", vm.QuickTasks[0].AssignedToUserName);
        Assert.Equal(12, vm.QuickTasks[0].AssignedToUserId);
    }

    [Fact]
    public async Task Create_dialog_defaults_assignee_for_non_admin()
    {
        var dialogVm = new TaskCreateDialogViewModel(
            new StubWorkbench(),
            new StubUser(10),
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            new StubAuthorization(admin: false));

        await dialogVm.InitializeAsync();

        Assert.NotNull(dialogVm.SelectedAssignee);
        Assert.Equal(10, dialogVm.SelectedAssignee!.Id);
        Assert.False(dialogVm.CanEditAssignee);
    }

    [Fact]
    public async Task No_fallback_to_all_users_when_my_tasks_empty()
    {
        var query = new RecordingScopeQueryService();
        var vm = CreateViewModel(admin: true, userId: 10, query: query);

        await vm.LoadAsync();

        Assert.Equal(TaskWorkbenchScope.MyTasks, vm.SelectedScope);
        Assert.Equal(0, query.AllUsersBucketCalls);
        Assert.Contains("לא נמצאו משימות", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_scope_has_no_LegacyBridge()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiNet.App.Wpf", "Surfaces", "Tasks", "TaskWorkbenchViewModel.cs"));
        Assert.DoesNotContain("LegacyBridge", source, StringComparison.Ordinal);
    }

    private static TaskWorkbenchViewModel CreateViewModel(
        bool admin,
        int userId,
        RecordingScopeQueryService? query = null)
    {
        query ??= new RecordingScopeQueryService();
        return new TaskWorkbenchViewModel(
            query,
            new StubNav(),
            null,
            new StubUser(userId),
            new StubAuthorization(admin),
            new StubUserLookup([new UserLookupDto(12, "User 12", true), new UserLookupDto(99, "User 99", true)]));
    }

    private static TaskSummaryDto CreateTask(int id, int userId, int bucket, string? userName = null) =>
        new(
            TaskId: id,
            ProjectId: 1,
            TaskTypeCode: "T",
            TaskTypeName: "Type",
            StatusCode: "Open",
            StatusName: "Open",
            IsOpen: true,
            AssignedToUserId: userId,
            AssignedToUserName: userName ?? $"User {userId}",
            WorkQueueBucket: bucket,
            WorkQueueBucketCode: WorkQueueBucketCodes.ToCode(bucket),
            WorkQueueBucketDisplayName: WorkQueueBucketCodes.ToDisplayName(bucket),
            WorkPriority: 1,
            DueDate: null,
            LastTaskResultCode: null,
            Title: $"Task {id}",
            ComponentKey: null);

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

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

    private sealed class StubNav : ITaskNavigationService
    {
        public ValueTask<Application.WorkSurfaces.WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult<Application.WorkSurfaces.WorkSurfaceContext?>(null);
    }

    private sealed class StubWorkbench : ITaskWorkbenchService
    {
        public IReadOnlyList<TaskLookupItemDto> OptionsProjects { get; } =
            [new TaskLookupItemDto(1, "Project 1")];

        public IReadOnlyList<TaskLookupItemDto> OptionsUsers { get; } =
            [new TaskLookupItemDto(10, "User 10"), new TaskLookupItemDto(12, "User 12")];

        public IReadOnlyList<TaskLookupItemDto> OptionsTaskTypes { get; } =
            [new TaskLookupItemDto(1, "Type 1")];

        public IReadOnlyList<TaskLookupItemDto> OptionsStatuses { get; } =
            [new TaskLookupItemDto(1, "Open")];

        public IReadOnlyList<TaskLookupItemDto> OptionsBuckets { get; } =
            [new TaskLookupItemDto(WorkQueueBucketCodes.Quick, "Quick")];

        public ValueTask<TaskCreationOptionsDto> GetTaskCreationOptionsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskCreationOptionsDto(
                OptionsProjects, OptionsUsers, OptionsTaskTypes, OptionsStatuses, OptionsBuckets));

        public ValueTask<TaskCommandResult> CreateTaskAsync(CreateTaskRequest request, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskCommandResult(true, "ok"));

        public ValueTask<TaskCommandResult> DeleteTaskAsync(int taskId, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskCommandResult(true, "ok"));

        public ValueTask<TaskCommandResult> DeactivateTaskAsync(int taskId, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskCommandResult(true, "ok"));

        public ValueTask<TaskCommandResult> ReactivateTaskAsync(int taskId, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskCommandResult(true, "ok"));

        public ValueTask<IReadOnlyList<int>> GetDemoTaskAssigneeUserIdsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<int>>([]);
    }

    private sealed class RecordingScopeQueryService : ITaskQueryService
    {
        public int? LastUserId { get; private set; }
        public int UserBucketCalls { get; private set; }
        public int AllUsersBucketCalls { get; private set; }

        public IReadOnlyList<TaskSummaryDto> AllUsersQuick { get; init; } = [];
        public IReadOnlyList<TaskSummaryDto> AllUsersMedium { get; init; } = [];
        public IReadOnlyList<TaskSummaryDto> AllUsersLong { get; init; } = [];

        public ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult<TaskSummaryDto?>(null);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(
            int projectId, bool includeClosed = false, int? workQueueBucket = null, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(
            int userId, int? workQueueBucket = null, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserByBucketAsync(
            int userId, int workQueueBucket, CancellationToken ct)
        {
            LastUserId = userId;
            UserBucketCalls++;
            return ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
        }

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(
            int workQueueBucket, CancellationToken ct)
        {
            AllUsersBucketCalls++;
            IReadOnlyList<TaskSummaryDto> result = workQueueBucket switch
            {
                WorkQueueBucketCodes.Quick => AllUsersQuick,
                WorkQueueBucketCodes.Medium => AllUsersMedium,
                WorkQueueBucketCodes.Long => AllUsersLong,
                _ => [],
            };
            return ValueTask.FromResult(result);
        }
    }
}
