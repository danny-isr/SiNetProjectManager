using Moq;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class ProjectWorkWindowViewModelTests
{
    private static WorkSurfaceContext ProjectWorkContext(
        int? taskId = 55,
        int projectId = 10,
        string[]? allowed = null,
        string? completionEventCode = "CompleteMaterial",
        int? actingUserId = 7) =>
        new(
            TaskId: taskId,
            ProjectId: projectId,
            WorkflowInstanceId: null,
            ComponentKey: WorkSurfaceComponentKeys.ProjectWork,
            PrimaryWorkTargetEntityId: null,
            AllowedResultCodes: allowed ?? new[] { "MaterialComplete" },
            CompletionEventCode: completionEventCode,
            ActingUserId: actingUserId);

    [Fact]
    public void Default_ctor_is_not_in_task_mode_and_has_no_file_workspace()
    {
        var sut = new ProjectWorkWindowViewModel();

        Assert.False(sut.IsTaskMode);
        Assert.False(sut.HasFileWorkspace);
        Assert.False(sut.CanCompleteTask);
    }

    [Fact]
    public async Task ApplyContextAsync_null_returns_false_and_stays_out_of_task_mode()
    {
        var sut = new ProjectWorkWindowViewModel(Mock.Of<ITaskCompletionService>());

        Assert.False(await sut.ApplyContextAsync(null));
        Assert.False(sut.IsTaskMode);
    }

    [Fact]
    public async Task ApplyContextAsync_rejects_non_project_work_component_key()
    {
        var sut = new ProjectWorkWindowViewModel(Mock.Of<ITaskCompletionService>());
        var context = ProjectWorkContext() with { ComponentKey = "Component.Inspection" };

        Assert.False(await sut.ApplyContextAsync(context));
        Assert.Contains("not the ProjectWork surface", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyContextAsync_rejects_context_without_project()
    {
        var sut = new ProjectWorkWindowViewModel(Mock.Of<ITaskCompletionService>());
        var context = ProjectWorkContext(projectId: 0);

        Assert.False(await sut.ApplyContextAsync(context));
        Assert.Contains("requires a project", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyContextAsync_valid_context_enters_task_mode_and_can_complete()
    {
        var sut = new ProjectWorkWindowViewModel(Mock.Of<ITaskCompletionService>());

        Assert.True(await sut.ApplyContextAsync(ProjectWorkContext()));
        Assert.True(sut.IsTaskMode);
        Assert.Equal("MaterialComplete", sut.SelectedResultCode);
        Assert.True(sut.CanCompleteTask);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_without_context_fails()
    {
        var sut = new ProjectWorkWindowViewModel(Mock.Of<ITaskCompletionService>());

        Assert.False(await sut.CompleteFromTaskAsync());
        Assert.Contains("not opened from a task", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_invokes_completion_service_and_reports_success()
    {
        var completion = new Mock<ITaskCompletionService>();
        CompleteTaskCommand? captured = null;
        completion
            .Setup(c => c.CompleteAsync(It.IsAny<CompleteTaskCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CompleteTaskCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new TaskCompletionResultDto(Success: true, TaskClosed: true, WorkflowAdvanced: false));

        var sut = new ProjectWorkWindowViewModel(completion.Object);
        await sut.ApplyContextAsync(ProjectWorkContext());

        Assert.True(await sut.CompleteFromTaskAsync());
        Assert.NotNull(captured);
        Assert.Equal(55, captured!.TaskId);
        Assert.Equal("CompleteMaterial", captured.CompletionEventCode);
        Assert.Equal("MaterialComplete", captured.TaskResultCode);
        Assert.Equal(7, captured.UserId);
        Assert.Contains("Completed task #55", sut.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_surfaces_rejection_from_service()
    {
        var completion = new Mock<ITaskCompletionService>();
        completion
            .Setup(c => c.CompleteAsync(It.IsAny<CompleteTaskCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskCompletionResultDto(Success: false, TaskClosed: false, WorkflowAdvanced: false, ErrorMessage: "rejected"));

        var sut = new ProjectWorkWindowViewModel(completion.Object);
        await sut.ApplyContextAsync(ProjectWorkContext());

        Assert.False(await sut.CompleteFromTaskAsync());
        Assert.Contains("rejected", sut.StatusMessage, StringComparison.Ordinal);
    }
}
