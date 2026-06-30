using SiNet.App.Wpf.Inspection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

/// <summary>
/// Unit tests for the one minimal completion path on <see cref="InspectionShellViewModel"/>
/// (<c>CompleteFromTaskAsync</c>). These lock in the workflow-first guarantees for completion:
/// the shell completes <b>only</b> through <see cref="ITaskCompletionService"/> (never touching
/// workflow itself), refuses to act with a clear message when there is no context / no task / no
/// resolvable result code, never invents a result code, and surfaces success/failure outcomes.
/// <para>
/// To populate the (private-set) <see cref="InspectionShellViewModel.Context"/> the tests drive the
/// official open path with a fake <see cref="ITaskNavigationService"/>; the real tree/notes view
/// models run against an empty fake <see cref="IInspectionWorkspace"/> (report selection is not the
/// subject here — the context is set before selection regardless).
/// </para>
/// </summary>
public sealed class InspectionShellViewModelCompletionTests
{
    private const string EventCode = "ReviewMaterialFiled";

    [Fact]
    public async Task CompleteFromTaskAsync_without_context_shows_message_and_does_not_call_service()
    {
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(navigation: new FakeNavigationService(null), completion: completion);

        // No OpenFromTaskAsync call -> Context is null.
        var ok = await sut.CompleteFromTaskAsync(EventCode, actingUserId: 1);

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_without_task_id_shows_message_and_does_not_call_service()
    {
        // Context resolves but has no TaskId (ad-hoc open): completion must refuse, clearly.
        var context = Context(taskId: null, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(EventCode, actingUserId: 1);

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_with_multiple_allowed_results_and_no_pick_refuses()
    {
        // Multiple allowed result codes and no explicit pick: must NOT guess — refuse with a message
        // and never call the completion service.
        var context = Context(taskId: 42, allowed: new[] { "Approved", "Rejected" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(EventCode, actingUserId: 1);

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_with_invented_result_code_refuses()
    {
        // An explicit result code that is not in AllowedResultCodes must be rejected (never invented).
        var context = Context(taskId: 42, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(EventCode, actingUserId: 1, taskResultCode: "NotAllowed");

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_with_single_allowed_result_calls_service_once_and_uses_it()
    {
        // Exactly one allowed code: it is used automatically and the service is called exactly once.
        var context = Context(taskId: 42, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(EventCode, actingUserId: 9);

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
        Assert.NotNull(completion.LastCommand);
        Assert.Equal(42, completion.LastCommand!.TaskId);
        Assert.Equal(EventCode, completion.LastCommand.CompletionEventCode);
        Assert.Equal("Approved", completion.LastCommand.TaskResultCode);
        Assert.Equal(9, completion.LastCommand.UserId);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_with_explicit_allowed_pick_uses_that_code()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved", "Rejected" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(EventCode, actingUserId: 1, taskResultCode: "Rejected");

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
        Assert.Equal("Rejected", completion.LastCommand!.TaskResultCode);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_with_failure_result_surfaces_error()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(
            TaskCompletionResultDto.Failure("Event requires a different result."));
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(EventCode, actingUserId: 1);

        Assert.False(ok);
        Assert.Equal(1, completion.CallCount);
        Assert.Contains("Event requires a different result.", sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_with_success_result_surfaces_success()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(new TaskCompletionResultDto(
            Success: true, TaskClosed: true, WorkflowAdvanced: true));
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(EventCode, actingUserId: 1);

        Assert.True(ok);
        Assert.Contains("Completed task #42", sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_without_event_code_refuses()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(completionEventCode: " ", actingUserId: 1);

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    private static InspectionShellViewModel BuildShell(
        ITaskNavigationService navigation, ITaskCompletionService completion)
    {
        var workspace = new EmptyWorkspace();
        return new InspectionShellViewModel(
            new InspectionTreeViewModel(workspace),
            new InspectionNotesViewModel(workspace),
            new InspectionDrawingsViewModel(),
            new InspectionReviewedPlanViewModel(),
            new InspectionReportViewModel(),
            navigation,
            completion);
    }

    private static WorkSurfaceContext Context(int? taskId, IReadOnlyList<string> allowed) =>
        new(
            TaskId: taskId,
            ProjectId: 10,
            WorkflowInstanceId: 5,
            ComponentKey: InspectionShellViewModel.InspectionComponentKey,
            PrimaryWorkTargetEntityId: null,
            AllowedResultCodes: allowed);

    private static TaskCompletionResultDto SuccessResult() =>
        new(Success: true, TaskClosed: true, WorkflowAdvanced: false);

    private sealed class FakeNavigationService : ITaskNavigationService
    {
        private readonly WorkSurfaceContext? _context;

        public FakeNavigationService(WorkSurfaceContext? context) => _context = context;

        public ValueTask<WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct)
            => ValueTask.FromResult(_context);
    }

    private sealed class FakeCompletionService : ITaskCompletionService
    {
        private readonly TaskCompletionResultDto _result;

        public FakeCompletionService(TaskCompletionResultDto result) => _result = result;

        public int CallCount { get; private set; }

        public CompleteTaskCommand? LastCommand { get; private set; }

        public ValueTask<TaskCompletionResultDto> CompleteAsync(CompleteTaskCommand command, CancellationToken ct)
        {
            CallCount++;
            LastCommand = command;
            return ValueTask.FromResult(_result);
        }
    }

    private sealed class EmptyWorkspace : IInspectionWorkspace
    {
        public Task<IReadOnlyList<InspectionSeriesSummary>> GetSeriesAsync(
            int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionSeriesSummary>>(Array.Empty<InspectionSeriesSummary>());

        public Task<IReadOnlyList<InspectionReportRow>> GetReportsAsync(
            int projectId, int seriesId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionReportRow>>(Array.Empty<InspectionReportRow>());

        public Task<IReadOnlyList<InspectionNoteRow>> GetNotesAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionNoteRow>>(Array.Empty<InspectionNoteRow>());
    }
}
