using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.LegacyBridge.Tasks;
using Xunit;

namespace SiNet.LegacyBridge.Tests;

/// <summary>
/// Unit tests for the strangler adapter <see cref="LegacyTaskCompletionService"/> that implements the
/// Application <c>ITaskCompletionService</c> port over the optional
/// <see cref="ILegacyTaskCompletionSource"/> seam. These lock in the completion-slice guarantees:
/// no host-bound source -> an <c>Unavailable</c> (non-success) result rather than a crash; a successful
/// legacy result maps field-for-field onto <see cref="TaskCompletionResultDto"/>; a failure legacy
/// result stays non-success; and the workflow auto-advance outcome (<see cref="StageCompletionResultDto"/>)
/// flows back through the seam unchanged.
/// </summary>
public sealed class LegacyTaskCompletionServiceTests
{
    private static CompleteTaskCommand Command() =>
        new(TaskId: 42, CompletionEventCode: "ReviewMaterialFiled", TaskResultCode: "Approved",
            CompletedTaskLinkIds: null, UserId: 7);

    [Fact]
    public async Task CompleteAsync_returns_Unavailable_when_source_is_unbound()
    {
        // New app host leaves the seam unbound: completion must report a clear, non-success
        // "unavailable" result (not throw, not silently succeed).
        var sut = new LegacyTaskCompletionService(source: null);

        var result = await sut.CompleteAsync(Command(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.TaskClosed);
        Assert.False(result.WorkflowAdvanced);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_maps_successful_legacy_result_field_for_field()
    {
        var stage = new StageCompletionResultDto(
            InstanceId: 5,
            CompletedStageId: 11,
            Action: StageCompletionActionDto.AutoAdvanced,
            TargetStageId: 12);

        var source = new FakeSource(new LegacyTaskCompletionResultDto(
            Success: true,
            TaskClosed: true,
            WorkflowAdvanced: true,
            ErrorMessage: null,
            NewProjectStatusId: 3,
            NewProjectStatusCode: "Active",
            RecordedTaskResultCode: "Approved",
            StageAdvanceResult: stage));
        var sut = new LegacyTaskCompletionService(source);

        var result = await sut.CompleteAsync(Command(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.TaskClosed);
        Assert.True(result.WorkflowAdvanced);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(3, result.NewProjectStatusId);
        Assert.Equal("Active", result.NewProjectStatusCode);
        Assert.Equal("Approved", result.RecordedTaskResultCode);
    }

    [Fact]
    public async Task CompleteAsync_forwards_command_to_source_unchanged()
    {
        // The adapter must hand the legacy seam exactly what the Application command carried — no
        // dropped/renamed fields — so the coordinator validates against the real inputs.
        var source = new FakeSource(SuccessResult());
        var sut = new LegacyTaskCompletionService(source);

        await sut.CompleteAsync(
            new CompleteTaskCommand(
                TaskId: 99,
                CompletionEventCode: "ReviewMaterialFiled",
                TaskResultCode: "Rejected",
                CompletedTaskLinkIds: new[] { 1, 2, 3 },
                UserId: 55),
            CancellationToken.None);

        Assert.NotNull(source.LastCommand);
        Assert.Equal(99, source.LastCommand!.TaskId);
        Assert.Equal("ReviewMaterialFiled", source.LastCommand.CompletionEventCode);
        Assert.Equal("Rejected", source.LastCommand.TaskResultCode);
        Assert.Equal(new[] { 1, 2, 3 }, source.LastCommand.CompletedTaskLinkIds);
        Assert.Equal(55, source.LastCommand.UserId);
    }

    [Fact]
    public async Task CompleteAsync_maps_failure_legacy_result_to_non_success()
    {
        // Ordinary validation/business failure: stays non-success and carries the message through
        // for the UI, without throwing.
        var source = new FakeSource(new LegacyTaskCompletionResultDto(
            Success: false,
            TaskClosed: false,
            WorkflowAdvanced: false,
            ErrorMessage: "Result 'X' is not allowed for event 'ReviewMaterialFiled'.",
            NewProjectStatusId: null,
            NewProjectStatusCode: null,
            RecordedTaskResultCode: null,
            StageAdvanceResult: null));
        var sut = new LegacyTaskCompletionService(source);

        var result = await sut.CompleteAsync(Command(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Result 'X' is not allowed for event 'ReviewMaterialFiled'.", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_preserves_stage_advance_result()
    {
        // The official IWorkflowCommandService auto-advance outcome must reach the UI unchanged so
        // it can reflect "workflow advanced" exactly as the coordinator decided.
        var stage = new StageCompletionResultDto(
            InstanceId: 8,
            CompletedStageId: 21,
            Action: StageCompletionActionDto.ManualAdvanceRequired,
            TargetStageId: 22,
            TransitionRuleId: 77);

        var source = new FakeSource(new LegacyTaskCompletionResultDto(
            Success: true,
            TaskClosed: true,
            WorkflowAdvanced: false,
            ErrorMessage: null,
            NewProjectStatusId: null,
            NewProjectStatusCode: null,
            RecordedTaskResultCode: "Approved",
            StageAdvanceResult: stage));
        var sut = new LegacyTaskCompletionService(source);

        var result = await sut.CompleteAsync(Command(), CancellationToken.None);

        Assert.NotNull(result.StageAdvanceResult);
        Assert.Same(stage, result.StageAdvanceResult);
        Assert.Equal(8, result.StageAdvanceResult!.InstanceId);
        Assert.Equal(21, result.StageAdvanceResult.CompletedStageId);
        Assert.Equal(StageCompletionActionDto.ManualAdvanceRequired, result.StageAdvanceResult.Action);
        Assert.Equal(22, result.StageAdvanceResult.TargetStageId);
        Assert.Equal(77, result.StageAdvanceResult.TransitionRuleId);
    }

    private static LegacyTaskCompletionResultDto SuccessResult() =>
        new(Success: true, TaskClosed: true, WorkflowAdvanced: false, ErrorMessage: null,
            NewProjectStatusId: null, NewProjectStatusCode: null, RecordedTaskResultCode: "Approved",
            StageAdvanceResult: null);

    private sealed class FakeSource : ILegacyTaskCompletionSource
    {
        private readonly LegacyTaskCompletionResultDto _result;

        public FakeSource(LegacyTaskCompletionResultDto result) => _result = result;

        public LegacyCompleteTaskCommandDto? LastCommand { get; private set; }

        public ValueTask<LegacyTaskCompletionResultDto> CompleteAsync(
            LegacyCompleteTaskCommandDto command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return ValueTask.FromResult(_result);
        }
    }
}
