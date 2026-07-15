using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.ProjectWork;

public sealed class ProjectWorkWindowViewModelTaskModeTests
{
    [Fact]
    public async Task ApplyContextAsync_binds_project_and_enables_completion()
    {
        var sut = new ProjectWorkWindowViewModel(new RecordingCompletion());

        var ok = await sut.ApplyContextAsync(CreateContext());

        Assert.True(ok);
        Assert.True(sut.IsTaskMode);
        Assert.True(sut.CanCompleteTask);
        Assert.Contains("MaterialComplete", sut.AllowedResultCodes);
        Assert.Contains("פרויקט 5", sut.ActiveProjectDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyContextAsync_wrong_component_key_is_rejected()
    {
        var sut = new ProjectWorkWindowViewModel(new RecordingCompletion());
        var context = CreateContext() with { ComponentKey = WorkSurfaceComponentKeys.InspectionReport };

        var ok = await sut.ApplyContextAsync(context);

        Assert.False(ok);
        Assert.False(sut.CanCompleteTask);
        Assert.Contains("not the ProjectWork surface", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyContextAsync_missing_project_blocks()
    {
        var sut = new ProjectWorkWindowViewModel(new RecordingCompletion());
        var context = CreateContext() with { ProjectId = 0 };

        var ok = await sut.ApplyContextAsync(context);

        Assert.False(ok);
        Assert.False(sut.CanCompleteTask);
        Assert.Contains("no project", sut.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(WorkSurfaceComponentKeys.MaterialChecklist)]
    [InlineData(WorkSurfaceComponentKeys.PoliceSubmission)]
    public async Task ApplyContextAsync_accepts_all_project_work_keys(string key)
    {
        var sut = new ProjectWorkWindowViewModel(new RecordingCompletion());
        var context = CreateContext() with { ComponentKey = key };

        var ok = await sut.ApplyContextAsync(context);

        Assert.True(ok);
        Assert.True(sut.IsTaskMode);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_blocks_when_user_unknown()
    {
        var completion = new RecordingCompletion();
        var sut = new ProjectWorkWindowViewModel(completion);
        var context = CreateContext() with { ActingUserId = null };

        Assert.True(await sut.ApplyContextAsync(context));
        var ok = await sut.CompleteFromTaskAsync();

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.Contains("acting user is unknown", sut.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_blocks_when_event_code_missing()
    {
        var completion = new RecordingCompletion();
        var sut = new ProjectWorkWindowViewModel(completion);
        var context = CreateContext() with
        {
            CompletionEventCode = null,
            TaskTypeCode = "UnknownType",
            AllowedResultCodes = [],
        };

        Assert.True(await sut.ApplyContextAsync(context));
        var ok = await sut.CompleteFromTaskAsync();

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.Contains("no completion event", sut.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_calls_completion_service_when_resolved()
    {
        var completion = new RecordingCompletion();
        var sut = new ProjectWorkWindowViewModel(completion);

        Assert.True(await sut.ApplyContextAsync(CreateContext()));
        sut.SelectedResultCode = "MaterialComplete";
        var ok = await sut.CompleteFromTaskAsync();

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
        Assert.Equal(10, completion.LastCommand?.TaskId);
        Assert.Equal("ReviewMaterialCheckCompleted", completion.LastCommand?.CompletionEventCode);
        Assert.Equal("MaterialComplete", completion.LastCommand?.TaskResultCode);
        Assert.Equal(7, completion.LastCommand?.UserId);
        Assert.Contains("Completed task #10", sut.StatusMessage, StringComparison.Ordinal);
    }

    private static WorkSurfaceContext CreateContext() =>
        new(
            TaskId: 10,
            ProjectId: 5,
            WorkflowInstanceId: 1,
            ComponentKey: WorkSurfaceComponentKeys.MaterialChecklist,
            PrimaryWorkTargetEntityId: null,
            AllowedResultCodes: ["MaterialComplete", "MaterialMissing"],
            CompletionEventCode: "ReviewMaterialCheckCompleted",
            ActingUserId: 7,
            TaskTypeCode: "CheckQuoteMaterialCompleteness");

    private sealed class RecordingCompletion : ITaskCompletionService
    {
        public int CallCount { get; private set; }
        public CompleteTaskCommand? LastCommand { get; private set; }

        public ValueTask<TaskCompletionResultDto> CompleteAsync(CompleteTaskCommand command, CancellationToken ct)
        {
            CallCount++;
            LastCommand = command;
            return ValueTask.FromResult(new TaskCompletionResultDto(
                Success: true,
                TaskClosed: true,
                WorkflowAdvanced: true));
        }
    }
}
