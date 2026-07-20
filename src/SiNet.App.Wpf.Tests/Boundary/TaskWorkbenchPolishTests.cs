using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.Application.Identity;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Task Workbench polish, queue move CanExecute, and task model documentation guards.</summary>
public sealed class TaskWorkbenchPolishTests
{
    [Fact]
    public void Task_workbench_uses_app_theme_resources()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var dialogXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskCreateDialogView.xaml");

        Assert.Contains("SiBackgroundBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("SiSecondaryButtonStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("SiPrimaryButtonStyle", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("SiTextSmallStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("SiTextLargeStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("SiComboBoxStyle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("#F5F5F5", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Foreground=\"#333\"", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkPriority_is_displayed_as_queue_position()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        Assert.Contains("Header=\"מיקום בתור\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding WorkPriority}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Priority_legacy_field_is_not_used_as_queue_position()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchViewModel.cs");
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");

        Assert.DoesNotContain("Binding=\"{Binding Priority}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(".Priority", vmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Move_up_command_disabled_for_first_task()
    {
        var vm = CreateMoveTestViewModel(
            SampleTask(1, workPriority: 1),
            SampleTask(2, workPriority: 2));

        vm.SelectedTask = vm.QuickTasks[0];
        Assert.False(CanExecute(vm.MoveUpCommand));
        Assert.True(CanExecute(vm.MoveDownCommand));
    }

    [Fact]
    public void Move_up_command_enabled_for_non_first_task()
    {
        var vm = CreateMoveTestViewModel(
            SampleTask(1, workPriority: 1),
            SampleTask(2, workPriority: 2));

        vm.SelectedTask = vm.QuickTasks[1];
        Assert.True(CanExecute(vm.MoveUpCommand));
        Assert.False(CanExecute(vm.MoveDownCommand));
    }

    [Fact]
    public void Move_down_command_disabled_for_last_task()
    {
        var vm = CreateMoveTestViewModel(
            SampleTask(1, workPriority: 1),
            SampleTask(2, workPriority: 2),
            SampleTask(3, workPriority: 3));

        vm.SelectedTask = vm.QuickTasks[2];
        Assert.True(CanExecute(vm.MoveUpCommand));
        Assert.False(CanExecute(vm.MoveDownCommand));
    }

    [Fact]
    public void Move_down_command_enabled_for_non_last_task()
    {
        var vm = CreateMoveTestViewModel(
            SampleTask(1, workPriority: 1),
            SampleTask(2, workPriority: 2),
            SampleTask(3, workPriority: 3));

        vm.SelectedTask = vm.QuickTasks[1];
        Assert.True(CanExecute(vm.MoveUpCommand));
        Assert.True(CanExecute(vm.MoveDownCommand));
    }

    [Fact]
    public void Move_commands_raise_can_execute_when_selection_changes()
    {
        var vm = CreateMoveTestViewModel(
            SampleTask(1, workPriority: 1),
            SampleTask(2, workPriority: 2));

        vm.SelectedTask = vm.QuickTasks[0];
        Assert.False(CanExecute(vm.MoveUpCommand));

        vm.SelectedTask = vm.QuickTasks[1];
        Assert.True(CanExecute(vm.MoveUpCommand));
    }

    [Fact]
    public void Move_commands_disabled_when_task_not_in_active_queue()
    {
        var vm = CreateMoveTestViewModel(SampleTask(1, workPriority: null));
        vm.SelectedTask = vm.QuickTasks[0];

        Assert.False(CanExecute(vm.MoveUpCommand));
        Assert.False(CanExecute(vm.MoveDownCommand));
        Assert.Equal("המשימה אינה נמצאת בתור פעיל.", vm.QueueMoveStatusHint);
    }

    [Fact]
    public async System.Threading.Tasks.Task Move_up_uses_ITaskQueueService_not_direct_WorkPriority_edit()
    {
        var queue = new RecordingTaskQueueService();
        var vm = CreateMoveTestViewModel(
            queue,
            SampleTask(1, workPriority: 1),
            SampleTask(2, workPriority: 2));
        vm.SelectedTask = vm.QuickTasks[1];

        vm.MoveUpCommand.Execute(null);
        await System.Threading.Tasks.Task.Delay(300);

        Assert.Equal(2, queue.LastMoveUpTaskId);
    }

    [Fact]
    public async System.Threading.Tasks.Task Repair_queue_button_uses_ITaskQueueService()
    {
        var queue = new RecordingTaskQueueService();
        var vm = new TaskWorkbenchViewModel(
            new EmptyTaskQuery(),
            new StubNav(),
            null,
            new StubUser(12),
            null,
            null,
            queue);

        vm.RepairQueueCommand.Execute(null);
        await System.Threading.Tasks.Task.Delay(500);

        Assert.True(queue.RepairCalled);
    }

    [Fact]
    public void Task_workbench_di_resolves_queue_service()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(
            new StubDbContextFactory(new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase("queue-di").Options));
        services.AddSiNetProcessBackbone();
        services.AddTransient<TaskWorkbenchViewModel>();
        services.AddSingleton<ICurrentUserContext>(new StubUser(12));
        using var provider = services.BuildServiceProvider();

        var vm = provider.GetRequiredService<TaskWorkbenchViewModel>();

        Assert.Equal("SqlTaskQueueService", vm.QueueServiceName);
        Assert.True(vm.CanManageQueue);
    }

    [Fact]
    public void Task_identity_rules_documented()
    {
        var doc = ReadRepoFile("docs/TASK_MODEL_RULES.md");
        Assert.Contains("ProjectAssignment", doc, StringComparison.Ordinal);
        Assert.Contains("WorkPriority", doc, StringComparison.Ordinal);
        Assert.Contains("WorkQueueBucket", doc, StringComparison.Ordinal);
        Assert.Contains("ParentAssignmentId", doc, StringComparison.Ordinal);
        Assert.Contains("ITaskQueueService", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Unique_open_task_index_documented()
    {
        var doc = ReadRepoFile("docs/TASK_MODEL_RULES.md");
        Assert.Contains("IX_ProjectAssignment_UniqueOpenTask", doc, StringComparison.Ordinal);
        Assert.Contains("ProjectId", doc, StringComparison.Ordinal);
        Assert.Contains("AssignedToId", doc, StringComparison.Ordinal);
        Assert.Contains("TaskTypeId", doc, StringComparison.Ordinal);
        Assert.Contains("WorkPriority IS NOT NULL", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Parent_child_task_rules_documented()
    {
        var doc = ReadRepoFile("docs/TASK_MODEL_RULES.md");
        Assert.Contains("ParentAssignmentId", doc, StringComparison.Ordinal);
        Assert.Contains("sub-task", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Task_workbench_has_no_LegacyBridge()
    {
        var csproj = ReadRepoFile("src/SiNet.App.Wpf/SiNet.App.Wpf.csproj");
        Assert.DoesNotContain("LegacyBridge", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_schema_change_or_migration_for_workbench_polish()
    {
        var snapshot = ReadRepoFile("src/SiNet.Infrastructure.Sql/Migrations/SiNetSQLDbContextModelSnapshot.cs");
        Assert.Contains("IX_ProjectAssignment_UniqueOpenTask", snapshot, StringComparison.Ordinal);
        Assert.Contains(
            "[ProjectID] IS NOT NULL AND [AssignedToID] IS NOT NULL AND [TaskTypeID] IS NOT NULL AND [WorkPriority] IS NOT NULL",
            snapshot,
            StringComparison.Ordinal);
    }

    private static TaskWorkbenchViewModel CreateMoveTestViewModel(params TaskSummaryDto[] tasks) =>
        CreateMoveTestViewModel(new RecordingTaskQueueService(), tasks);

    private static TaskWorkbenchViewModel CreateMoveTestViewModel(RecordingTaskQueueService queue, params TaskSummaryDto[] tasks)
    {
        var query = new BucketTaskQuery(tasks);
        var vm = new TaskWorkbenchViewModel(
            query,
            new StubNav(),
            null,
            new StubUser(12),
            null,
            null,
            queue);

        vm.LoadAsync().GetAwaiter().GetResult();
        return vm;
    }

    private static TaskSummaryDto SampleTask(int taskId, int? workPriority) =>
        new(
            TaskId: taskId,
            ProjectId: 1,
            TaskTypeCode: "T",
            TaskTypeName: "Type",
            StatusCode: "Open",
            StatusName: "Open",
            IsOpen: true,
            AssignedToUserId: 12,
            AssignedToUserName: "User 12",
            WorkQueueBucket: WorkQueueBucketCodes.Quick,
            WorkQueueBucketCode: WorkQueueBucketCodes.ToCode(WorkQueueBucketCodes.Quick),
            WorkQueueBucketDisplayName: WorkQueueBucketCodes.ToDisplayName(WorkQueueBucketCodes.Quick),
            WorkPriority: workPriority,
            DueDate: null,
            CreatedAt: null,
            LastTaskResultCode: null,
            Title: $"Task {taskId}",
            ComponentKey: null);

    private static bool CanExecute(System.Windows.Input.ICommand command) =>
        command.CanExecute(null);

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class StubUser(int id) : ICurrentUserContext
    {
        public int? UserId { get; } = id;
    }

    private sealed class StubNav : ITaskNavigationService
    {
        public ValueTask<Application.WorkSurfaces.WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult<Application.WorkSurfaces.WorkSurfaceContext?>(null);
    }

    private sealed class EmptyTaskQuery : ITaskQueryService
    {
        public ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult<TaskSummaryDto?>(null);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(
            int projectId, bool includeClosed = false, int? workQueueBucket = null, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(
            int userId, int? workQueueBucket = null, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserByBucketAsync(
            int userId, int workQueueBucket, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(
            int workQueueBucket, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
    }

    private sealed class BucketTaskQuery(IReadOnlyList<TaskSummaryDto> quick) : ITaskQueryService
    {
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
            IReadOnlyList<TaskSummaryDto> result = workQueueBucket == WorkQueueBucketCodes.Quick ? quick : [];
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(
            int workQueueBucket, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
    }

    private sealed class RecordingTaskQueueService : ITaskQueueService
    {
        public int? LastMoveUpTaskId { get; private set; }
        public bool RepairCalled { get; private set; }

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetUserQueueAsync(int userId, int workQueueBucket, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask MoveWithinBucketAsync(int taskId, int newPosition, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public ValueTask ChangeBucketAsync(int taskId, int newBucket, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public ValueTask<int> ValidateAndRepairQueueAsync(int userId, int workQueueBucket, CancellationToken ct = default) =>
            ValueTask.FromResult(0);

        public ValueTask<TaskQueueRepairResult> RepairQueueAsync(int userId, int workQueueBucket, CancellationToken ct = default)
        {
            RepairCalled = true;
            return ValueTask.FromResult(TaskQueueRepairResult.Empty);
        }

        public ValueTask<TaskQueueRepairResult> RepairAllQueuesAsync(CancellationToken ct = default)
        {
            RepairCalled = true;
            return ValueTask.FromResult(TaskQueueRepairResult.Empty);
        }

        public ValueTask<TaskQueueOperationResult> MoveUpAsync(int taskId, int changedByUserId, CancellationToken ct = default)
        {
            LastMoveUpTaskId = taskId;
            return ValueTask.FromResult(new TaskQueueOperationResult(true, "ok", taskId, OldPriority: 2, NewPriority: 1));
        }

        public ValueTask<TaskQueueOperationResult> MoveDownAsync(int taskId, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskQueueOperationResult(true, "ok", taskId, OldPriority: 1, NewPriority: 2));

        public ValueTask<TaskQueueOperationResult> ReassignAsync(int taskId, int newUserId, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskQueueOperationResult(true, "ok", taskId));
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options) : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);
    }
}
