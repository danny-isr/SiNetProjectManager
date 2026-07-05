using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards for the read-only Task Panel pilot — Application ports only, no writes, no legacy panel.
/// </summary>
public sealed class TaskPanelReadOnlyTests
{
    private static readonly string[] ForbiddenWriteIdentifiers =
    [
        "CompleteTask",
        "ChangeBucket",
        "MoveWithinBucket",
        "MoveToProject",
        "AddMaterial",
        "ProcessAction",
        "ITaskQueueService",
        "ChangeTaskBucket",
    ];

    private static readonly string[] ForbiddenLegacyIdentifiers =
    [
        "TaskPanelViewModel",
        "LegacyBridge",
        "TaskWindowRouter",
        "NewTaskWindowRouter",
        "SiNetSQL",
    ];

    [Fact]
    public void Task_panel_read_only_viewmodel_uses_ITaskQueryService()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchViewModel.cs");

        Assert.Contains("ITaskQueryService", source, StringComparison.Ordinal);
        Assert.Contains("GetOpenTasksForUserByBucketAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetTasksForProjectAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Task_panel_loads_three_bucket_queues()
    {
        var query = new RecordingTaskQueryService();
        var sut = new TaskWorkbenchViewModel(
            query,
            new StubTaskNavigationService(),
            null,
            new StubCurrentUserContext(42),
            null);

        await sut.LoadAsync();

        Assert.Equal(3, query.BucketCalls.Count);
        Assert.Equal(WorkQueueBucketCodes.Quick, query.BucketCalls[0]);
        Assert.Equal(WorkQueueBucketCodes.Medium, query.BucketCalls[1]);
        Assert.Equal(WorkQueueBucketCodes.Long, query.BucketCalls[2]);
        Assert.Equal(42, query.LastUserId);
    }

    [Fact]
    public async Task Task_panel_displays_bucket_metadata()
    {
        var task = SampleTask(WorkQueueBucketCodes.Quick, workPriority: 1);
        var query = new RecordingTaskQueryService
        {
            QuickResult = [task],
        };

        var sut = new TaskWorkbenchViewModel(
            query,
            new StubTaskNavigationService(),
            null,
            new StubCurrentUserContext(7),
            null);

        await sut.LoadAsync();

        Assert.Single(sut.QuickTasks);
        var loaded = sut.QuickTasks[0];
        Assert.Equal(WorkQueueBucketCodes.Quick, loaded.WorkQueueBucket);
        Assert.Equal("Quick", loaded.WorkQueueBucketCode);
        Assert.Equal("Quick / קצר", loaded.WorkQueueBucketDisplayName);
        Assert.Equal(1, loaded.WorkPriority);
    }

    [Fact]
    public async Task Task_panel_resolve_uses_ITaskNavigationService()
    {
        var context = new WorkSurfaceContext(
            TaskId: 99,
            ProjectId: 10,
            WorkflowInstanceId: 5,
            ComponentKey: "Inspection",
            PrimaryWorkTargetEntityId: 77,
            AllowedResultCodes: ["Done"],
            CompletionEventCode: "CompleteInspection",
            ActingUserId: 3,
            TaskTypeCode: "Inspect");

        var navigation = new StubTaskNavigationService { ResolveResult = context };
        var query = new RecordingTaskQueryService { QuickResult = [SampleTask(WorkQueueBucketCodes.Quick, taskId: 99)] };
        var sut = new TaskWorkbenchViewModel(
            query,
            navigation,
            null,
            new StubCurrentUserContext(3),
            null);

        await sut.LoadAsync();
        sut.SelectedTask = sut.QuickTasks[0];
        await sut.ResolveSelectedAsync();

        Assert.Equal(99, navigation.LastResolvedTaskId);
        Assert.Contains("ComponentKey: Inspection", sut.ResolvePreview, StringComparison.Ordinal);
        Assert.Contains("TaskId: 99", sut.ResolvePreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Task_panel_resolve_null_context_shows_clear_error()
    {
        var navigation = new StubTaskNavigationService { ResolveResult = null };
        var query = new RecordingTaskQueryService { QuickResult = [SampleTask(WorkQueueBucketCodes.Quick, taskId: 1)] };
        var sut = new TaskWorkbenchViewModel(
            query,
            navigation,
            null,
            new StubCurrentUserContext(1),
            null);

        await sut.LoadAsync();
        sut.SelectedTask = sut.QuickTasks[0];
        await sut.ResolveSelectedAsync();

        Assert.Equal(
            "לא ניתן לפתוח את המשימה דרך WorkSurfaceContext. אין fallback.",
            sut.ResolvePreview);
    }

    [Fact]
    public void Task_workbench_uses_ITaskWorkbenchService_for_writes()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchViewModel.cs");
        Assert.Contains("ITaskWorkbenchService", source, StringComparison.Ordinal);
        Assert.Contains("CreateTaskAsync", source, StringComparison.Ordinal);
        Assert.Contains("DeleteTaskAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQLDbContext", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_panel_has_no_direct_SiNetSQL_dependency()
    {
        var csproj = ReadRepoFile("src/SiNet.App.Wpf/SiNet.App.Wpf.csproj");
        Assert.DoesNotContain("SiNetSQL", csproj, StringComparison.OrdinalIgnoreCase);

        foreach (var relativePath in EnumerateTaskPanelFiles())
        {
            var content = ReadRepoFile(relativePath);
            foreach (var forbidden in ForbiddenLegacyIdentifiers)
            {
                Assert.False(content.Contains(forbidden, StringComparison.Ordinal),
                    $"'{forbidden}' found in {relativePath}");
            }
        }
    }

    [Fact]
    public void NewShell_task_panel_entry_is_read_only()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("ITaskPanelReadOnlyWindowFactory", source, StringComparison.Ordinal);
        Assert.Contains("Task Workbench", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.ShellOpenTaskPanelReadOnly", source, StringComparison.Ordinal);
        Assert.Contains("taskPanelFactory.Create()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_panel_does_not_open_legacy_TaskPanel()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchViewModel.cs");
        var factorySource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskPanelReadOnlyWindowFactory.cs");

        Assert.DoesNotContain("TaskPanelViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TaskPanelViewModel", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TaskPanelViewModel", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("FloatingTasks", source, StringComparison.Ordinal);
    }

    [Fact]
    public void New_system_wpf_registers_task_panel_read_only()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/NewSystemWpfServiceCollectionExtensions.cs");
        Assert.Contains("AddSiNetTaskPanelReadOnly()", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Task_panel_without_user_or_project_shows_guidance_message()
    {
        var sut = new TaskWorkbenchViewModel(
            new RecordingTaskQueryService(),
            new StubTaskNavigationService(),
            null,
            null,
            new InMemoryCurrentProjectContext());

        await sut.LoadAsync();

        Assert.Equal("בחר פרויקט או התחבר כמשתמש כדי לראות משימות.", sut.StatusMessage);
    }

    [Fact]
    public async Task Task_panel_empty_user_queue_shows_clear_message()
    {
        var sut = new TaskWorkbenchViewModel(
            new RecordingTaskQueryService(),
            new StubTaskNavigationService(),
            null,
            new StubCurrentUserContext(99),
            null);

        await sut.LoadAsync();

        Assert.Equal(
            "לא נמצאו משימות עבור UserId=99. ייתכן שמשימות הדemo נוצרו למשתמש אחר.",
            sut.StatusMessage);
    }

    [Fact]
    public void Task_panel_user_status_message_includes_bucket_counts()
    {
        var message = TaskWorkbenchViewModel.FormatUserStatusMessage(
            42,
            new TaskWorkbenchViewModel.BucketCounts(3, 2, 1));

        Assert.Equal("נטענו 6 משימות למשתמש 42: קצר=3, בינוני=2, ארוך=1", message);
    }

    private static TaskSummaryDto SampleTask(int bucket, int taskId = 100, int? workPriority = 5) =>
        new(
            TaskId: taskId,
            ProjectId: 10,
            TaskTypeCode: "T1",
            TaskTypeName: "Type One",
            StatusCode: "Open",
            StatusName: "Open",
            IsOpen: true,
            AssignedToUserId: 7,
            AssignedToUserName: "User",
            WorkQueueBucket: bucket,
            WorkQueueBucketCode: WorkQueueBucketCodes.ToCode(bucket),
            WorkQueueBucketDisplayName: WorkQueueBucketCodes.ToDisplayName(bucket),
            WorkPriority: workPriority,
            DueDate: new DateTime(2026, 7, 10),
            LastTaskResultCode: null,
            Title: "Sample task",
            ComponentKey: "Email");

    private static IEnumerable<string> EnumerateTaskPanelFiles()
    {
        var dir = Path.Combine(ResolveRepoRoot(), "src", "SiNet.App.Wpf", "Surfaces", "Tasks");
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.GetRelativePath(ResolveRepoRoot(), file).Replace('\\', '/');
            }
        }
    }

    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(Path.Combine(ResolveRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "UI_WINDOW_MIGRATION_MAP.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class RecordingTaskQueryService : ITaskQueryService
    {
        public List<int> BucketCalls { get; } = [];
        public int? LastUserId { get; private set; }

        public IReadOnlyList<TaskSummaryDto> QuickResult { get; init; } = [];
        public IReadOnlyList<TaskSummaryDto> MediumResult { get; init; } = [];
        public IReadOnlyList<TaskSummaryDto> LongResult { get; init; } = [];

        public ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct)
            => ValueTask.FromResult<TaskSummaryDto?>(null);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(
            int projectId, bool includeClosed = false, int? workQueueBucket = null, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(
            int userId, int? workQueueBucket = null, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserByBucketAsync(
            int userId, int workQueueBucket, CancellationToken ct)
        {
            LastUserId = userId;
            BucketCalls.Add(workQueueBucket);
            IReadOnlyList<TaskSummaryDto> result = workQueueBucket switch
            {
                WorkQueueBucketCodes.Quick => QuickResult,
                WorkQueueBucketCodes.Medium => MediumResult,
                WorkQueueBucketCodes.Long => LongResult,
                _ => [],
            };
            return ValueTask.FromResult(result);
        }

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(
            int workQueueBucket, CancellationToken ct)
            => ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
    }

    private sealed class StubTaskNavigationService : ITaskNavigationService
    {
        public int? LastResolvedTaskId { get; private set; }
        public WorkSurfaceContext? ResolveResult { get; init; }

        public ValueTask<WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct)
        {
            LastResolvedTaskId = taskId;
            return ValueTask.FromResult(ResolveResult);
        }
    }

    private sealed class StubCurrentUserContext(int userId) : ICurrentUserContext
    {
        public int? UserId { get; } = userId;
    }
}
