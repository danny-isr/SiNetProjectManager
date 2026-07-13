using SiNet.App.Wpf.Inspection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Identity;
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

    // ----- Minimal UI-trigger (CompleteTaskCommand) behavior -----
    // The command is the one minimal UI entry point. It reads the admin/dev inputs
    // (CompletionEventCode, ActingUserId, SelectedResultCode) and delegates to
    // CompleteFromTaskAsync, which holds all the guardrails. The fake completion service is
    // synchronous, so the command's continuation runs to completion before Execute returns.

    [Fact]
    public void CompleteTaskCommand_is_disabled_when_not_in_task_mode()
    {
        // Fresh shell, never opened from a task: completion must not be offered.
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(null), completion);

        Assert.False(sut.IsTaskMode);
        Assert.False(sut.CanCompleteInTaskMode);
        Assert.False(sut.CompleteTaskCommand.CanExecute(null));
    }

    [Fact]
    public async Task CompleteTaskCommand_is_disabled_without_task_id()
    {
        // Task mode but the context has no TaskId (ad-hoc open): completion must not be offered.
        var context = Context(taskId: null, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.False(sut.CanCompleteInTaskMode);
        Assert.False(sut.CompleteTaskCommand.CanExecute(null));
    }

    [Fact]
    public async Task CompleteTaskCommand_is_enabled_with_real_task_context()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.True(sut.CanCompleteInTaskMode);
        Assert.True(sut.CompleteTaskCommand.CanExecute(null));
    }

    [Fact]
    public void CompleteTaskCommand_exposes_no_result_picker_without_context()
    {
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(null), completion);

        Assert.Empty(sut.AllowedResultCodes);
        Assert.False(sut.HasMultipleAllowedResultCodes);
    }

    [Fact]
    public async Task CompleteTaskCommand_surfaces_allowed_codes_and_picker_only_when_multiple()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved", "Rejected" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.Equal(new[] { "Approved", "Rejected" }, sut.AllowedResultCodes);
        Assert.True(sut.HasMultipleAllowedResultCodes);
    }

    [Fact]
    public async Task CompleteTaskCommand_without_event_code_does_not_call_service()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        // Admin/dev event-code input left blank.
        sut.CompletionEventCode = "   ";
        sut.ActingUserId = 7;
        sut.CompleteTaskCommand.Execute(null);

        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteTaskCommand_with_multiple_results_and_no_pick_does_not_call_service()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved", "Rejected" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        sut.CompletionEventCode = EventCode;
        sut.ActingUserId = 7;
        // No SelectedResultCode chosen.
        sut.CompleteTaskCommand.Execute(null);

        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteTaskCommand_with_invalid_selected_result_does_not_call_service()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved", "Rejected" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        sut.CompletionEventCode = EventCode;
        sut.ActingUserId = 7;
        sut.SelectedResultCode = "NotAllowed";
        sut.CompleteTaskCommand.Execute(null);

        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteTaskCommand_with_valid_inputs_calls_service_once_and_forwards()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved", "Rejected" });
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        sut.CompletionEventCode = EventCode;
        sut.ActingUserId = 7;
        sut.SelectedResultCode = "Rejected";
        sut.CompleteTaskCommand.Execute(null);

        Assert.Equal(1, completion.CallCount);
        Assert.NotNull(completion.LastCommand);
        Assert.Equal(42, completion.LastCommand!.TaskId);
        Assert.Equal(EventCode, completion.LastCommand.CompletionEventCode);
        Assert.Equal("Rejected", completion.LastCommand.TaskResultCode);
        Assert.Equal(7, completion.LastCommand.UserId);
    }

    [Fact]
    public async Task CompleteTaskCommand_with_success_result_surfaces_success_message()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(new TaskCompletionResultDto(
            Success: true, TaskClosed: true, WorkflowAdvanced: true));
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        sut.CompletionEventCode = EventCode;
        sut.ActingUserId = 7;
        sut.CompleteTaskCommand.Execute(null);

        Assert.Equal(1, completion.CallCount);
        Assert.Contains("Completed task #42", sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteTaskCommand_with_failure_result_surfaces_error_message()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved" });
        var completion = new FakeCompletionService(
            TaskCompletionResultDto.Failure("Event requires a different result."));
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        sut.CompletionEventCode = EventCode;
        sut.ActingUserId = 7;
        sut.CompleteTaskCommand.Execute(null);

        Assert.Equal(1, completion.CallCount);
        Assert.Contains("Event requires a different result.", sut.TaskStatusMessage);
    }

    // ----- Resolution of CompletionEventCode and ActingUserId from real context -----
    // These lock in that the shell auto-fills each value when it can be resolved safely (and hides the
    // dev input), keeps the dev input when it cannot, and never guesses.

    [Fact]
    public async Task OpenFromTaskAsync_resolved_event_code_is_used_and_hides_dev_input()
    {
        // Context carries an unambiguous event code -> auto-filled, dev input hidden.
        var context = Context(taskId: 42, allowed: new[] { "Approved" }, completionEventCode: EventCode);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.Equal(EventCode, sut.CompletionEventCode);
        Assert.False(sut.NeedsManualCompletionEventCode);

        // And the resolved code flows to the service on completion.
        var ok = await sut.CompleteFromTaskAsync(sut.CompletionEventCode!, actingUserId: 9);
        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
        Assert.Equal(EventCode, completion.LastCommand!.CompletionEventCode);
    }

    [Fact]
    public async Task OpenFromTaskAsync_missing_event_code_keeps_dev_input()
    {
        // No event code on the context (ambiguous task type) -> dev input remains.
        var context = Context(taskId: 42, allowed: new[] { "Approved" }, completionEventCode: null);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.True(sut.NeedsManualCompletionEventCode);
        Assert.True(string.IsNullOrWhiteSpace(sut.CompletionEventCode));
    }

    [Fact]
    public async Task OpenFromTaskAsync_resolved_user_id_from_context_is_used_and_hides_dev_input()
    {
        var context = Context(taskId: 42, allowed: new[] { "Approved" }, actingUserId: 77);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.Equal(77, sut.ActingUserId);
        Assert.False(sut.NeedsManualActingUserId);
    }

    [Fact]
    public async Task OpenFromTaskAsync_resolved_user_id_from_host_context_is_used_when_context_has_none()
    {
        // Context has no user id, but the host ICurrentUserContext does -> use it, hide dev input.
        var context = Context(taskId: 42, allowed: new[] { "Approved" }, actingUserId: null);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion, new FakeCurrentUserContext(55));
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.Equal(55, sut.ActingUserId);
        Assert.False(sut.NeedsManualActingUserId);
    }

    [Fact]
    public async Task OpenFromTaskAsync_missing_user_id_keeps_dev_input()
    {
        // Neither the context nor the host user context yields an id -> dev input remains.
        var context = Context(taskId: 42, allowed: new[] { "Approved" }, actingUserId: null);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion, new FakeCurrentUserContext(null));
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.True(sut.NeedsManualActingUserId);
    }

    [Fact]
    public async Task OpenFromTaskAsync_non_positive_host_user_id_keeps_dev_input()
    {
        // A zero/negative id is treated as "unknown" and must not be accepted.
        var context = Context(taskId: 42, allowed: new[] { "Approved" }, actingUserId: 0);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion, new FakeCurrentUserContext(0));
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.True(sut.NeedsManualActingUserId);
    }

    [Fact]
    public async Task OpenFromTaskAsync_with_both_resolved_calls_service_once_with_resolved_values()
    {
        // Both values resolved from context: the command path uses them and calls the service once.
        var context = Context(
            taskId: 42, allowed: new[] { "Approved" }, completionEventCode: EventCode, actingUserId: 88);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion);
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.False(sut.NeedsManualCompletionEventCode);
        Assert.False(sut.NeedsManualActingUserId);

        sut.CompleteTaskCommand.Execute(null);

        Assert.Equal(1, completion.CallCount);
        Assert.Equal(42, completion.LastCommand!.TaskId);
        Assert.Equal(EventCode, completion.LastCommand.CompletionEventCode);
        Assert.Equal(88, completion.LastCommand.UserId);
    }

    // ----- Branching CompletionEventCode resolution from the selected result code -----
    // For task types whose result selects between several events (e.g. RecheckPlan), the event code is
    // resolved from (TaskTypeCode, resolved result) via the metadata port AFTER the result is known.
    // The ViewModel owns no mapping table; it consumes the resolver's answer and blocks (no service
    // call) whenever the pair is missing/invalid/ambiguous.

    private const string RecheckPlan = "RecheckPlan";
    private const string RecheckPassed = "RecheckPassed";
    private const string RecheckNeedsMore = "RecheckRequiresMoreCorrections";
    private const string EventPassed = "ReviewRecheckPassed";
    private const string EventNeedsMore = "ReviewRecheckRequiresMoreCorrections";

    private static FakeCompletionMetadataResolver RecheckResolver() =>
        new(new Dictionary<(string, string), string>
        {
            [(RecheckPlan, RecheckPassed)] = EventPassed,
            [(RecheckPlan, RecheckNeedsMore)] = EventNeedsMore,
        });

    [Fact]
    public async Task CompleteFromTaskAsync_explicit_event_code_wins_over_resolution()
    {
        // An explicit event code always takes precedence; the resolver is not consulted.
        var context = Context(
            taskId: 42, allowed: new[] { RecheckPassed, RecheckNeedsMore }, taskTypeCode: RecheckPlan);
        var completion = new FakeCompletionService(SuccessResult());
        var resolver = RecheckResolver();
        var sut = BuildShell(new FakeNavigationService(context), completion, completionMetadata: resolver);
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync("ExplicitEvent", actingUserId: 9, taskResultCode: RecheckPassed);

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
        Assert.Equal("ExplicitEvent", completion.LastCommand!.CompletionEventCode);
        Assert.Equal(RecheckPassed, completion.LastCommand.TaskResultCode);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_unique_context_event_code_still_works()
    {
        // The unambiguous-by-task-type path (event projected on the context) is unchanged.
        var context = Context(taskId: 42, allowed: new[] { "Approved" }, completionEventCode: EventCode);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion, completionMetadata: RecheckResolver());
        await sut.OpenFromTaskAsync(taskId: 42);

        Assert.False(sut.NeedsManualCompletionEventCode);

        sut.CompleteTaskCommand.Execute(null);

        Assert.Equal(1, completion.CallCount);
        Assert.Equal(EventCode, completion.LastCommand!.CompletionEventCode);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_branching_result_A_resolves_event_A()
    {
        var context = Context(
            taskId: 42, allowed: new[] { RecheckPassed, RecheckNeedsMore }, taskTypeCode: RecheckPlan);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion, completionMetadata: RecheckResolver());
        await sut.OpenFromTaskAsync(taskId: 42);

        // No explicit event code: it must be resolved from (RecheckPlan, RecheckPassed).
        var ok = await sut.CompleteFromTaskAsync(string.Empty, actingUserId: 9, taskResultCode: RecheckPassed);

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
        Assert.Equal(EventPassed, completion.LastCommand!.CompletionEventCode);
        Assert.Equal(RecheckPassed, completion.LastCommand.TaskResultCode);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_branching_result_B_resolves_event_B()
    {
        var context = Context(
            taskId: 42, allowed: new[] { RecheckPassed, RecheckNeedsMore }, taskTypeCode: RecheckPlan);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion, completionMetadata: RecheckResolver());
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(string.Empty, actingUserId: 9, taskResultCode: RecheckNeedsMore);

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
        Assert.Equal(EventNeedsMore, completion.LastCommand!.CompletionEventCode);
        Assert.Equal(RecheckNeedsMore, completion.LastCommand.TaskResultCode);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_branching_without_result_blocks_completion()
    {
        // Multiple allowed results and no pick: TryResolveResultCode blocks before event resolution, so
        // the service is never called.
        var context = Context(
            taskId: 42, allowed: new[] { RecheckPassed, RecheckNeedsMore }, taskTypeCode: RecheckPlan);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion, completionMetadata: RecheckResolver());
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(string.Empty, actingUserId: 9);

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_branching_with_invalid_result_blocks_completion()
    {
        var context = Context(
            taskId: 42, allowed: new[] { RecheckPassed, RecheckNeedsMore }, taskTypeCode: RecheckPlan);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion, completionMetadata: RecheckResolver());
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(string.Empty, actingUserId: 9, taskResultCode: "NotAllowed");

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_unmapped_pair_blocks_before_service_call()
    {
        // The pair is allowed by the context but the resolver has no event for it: block, no call.
        var context = Context(
            taskId: 42, allowed: new[] { RecheckPassed, RecheckNeedsMore }, taskTypeCode: RecheckPlan);
        var completion = new FakeCompletionService(SuccessResult());
        // Resolver knows only result A -> event A; result B is unmapped (ambiguous/unsupported).
        var resolver = new FakeCompletionMetadataResolver(new Dictionary<(string, string), string>
        {
            [(RecheckPlan, RecheckPassed)] = EventPassed,
        });
        var sut = BuildShell(new FakeNavigationService(context), completion, completionMetadata: resolver);
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(string.Empty, actingUserId: 9, taskResultCode: RecheckNeedsMore);

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.NotNull(sut.TaskStatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_valid_resolved_pair_calls_service_once()
    {
        var context = Context(
            taskId: 42, allowed: new[] { RecheckPassed, RecheckNeedsMore }, taskTypeCode: RecheckPlan);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion, completionMetadata: RecheckResolver());
        await sut.OpenFromTaskAsync(taskId: 42);

        var ok = await sut.CompleteFromTaskAsync(string.Empty, actingUserId: 9, taskResultCode: RecheckPassed);

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
    }

    [Fact]
    public async Task CompleteTaskCommand_branching_resolves_event_and_hides_dev_input_after_pick()
    {
        // End-to-end through the command: a branching task hides the dev event input once a valid result
        // is selected (because the event can then be resolved), and forwards the resolved event once.
        var context = Context(
            taskId: 42, allowed: new[] { RecheckPassed, RecheckNeedsMore }, taskTypeCode: RecheckPlan);
        var completion = new FakeCompletionService(SuccessResult());
        var sut = BuildShell(new FakeNavigationService(context), completion, completionMetadata: RecheckResolver());
        await sut.OpenFromTaskAsync(taskId: 42);

        // Before picking a result the dev input is still needed (the event is not yet resolvable).
        Assert.True(sut.NeedsManualCompletionEventCode);

        sut.ActingUserId = 7;
        sut.SelectedResultCode = RecheckPassed;

        // After a valid pick the event resolves, so the dev input is no longer needed.
        Assert.False(sut.NeedsManualCompletionEventCode);

        sut.CompleteTaskCommand.Execute(null);

        Assert.Equal(1, completion.CallCount);
        Assert.Equal(EventPassed, completion.LastCommand!.CompletionEventCode);
        Assert.Equal(RecheckPassed, completion.LastCommand.TaskResultCode);
        Assert.Equal(7, completion.LastCommand.UserId);
    }

    private static InspectionShellViewModel BuildShell(
        ITaskNavigationService navigation,
        ITaskCompletionService completion,
        ICurrentUserContext? currentUser = null,
        ITaskCompletionMetadataResolver? completionMetadata = null)
    {
        var workspace = new EmptyWorkspace();
        return new InspectionShellViewModel(
            new InspectionTreeViewModel(workspace),
            new InspectionNotesViewModel(workspace),
            new InspectionDrawingsViewModel(),
            new InspectionReviewedPlanViewModel(),
            new InspectionReportViewModel(),
            navigation,
            completion,
            currentUser,
            completionMetadata);
    }

    private static WorkSurfaceContext Context(
        int? taskId,
        IReadOnlyList<string> allowed,
        string? completionEventCode = null,
        int? actingUserId = null,
        string? taskTypeCode = null) =>
        new(
            TaskId: taskId,
            ProjectId: 10,
            WorkflowInstanceId: 5,
            ComponentKey: InspectionShellViewModel.InspectionComponentKey,
            PrimaryWorkTargetEntityId: null,
            AllowedResultCodes: allowed,
            CompletionEventCode: completionEventCode,
            ActingUserId: actingUserId,
            TaskTypeCode: taskTypeCode);

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

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public FakeCurrentUserContext(int? userId) => UserId = userId;

        public int? UserId { get; }
    }

    /// <summary>
    /// Deterministic stand-in for the host completion-metadata port. It mirrors the legacy
    /// declarative mapping for the branching case under test (<c>RecheckPlan</c>) without depending on
    /// SiNetSQL: it resolves the event only for an exact, supported <c>(task type, result)</c> pair and
    /// returns <see langword="null"/> (block) for everything else, including the no-result and ambiguous
    /// cases. The constructor takes the rows so each test states exactly which pairs are supported.
    /// </summary>
    private sealed class FakeCompletionMetadataResolver : ITaskCompletionMetadataResolver
    {
        private readonly IReadOnlyDictionary<(string TaskType, string Result), string> _byPair;
        private readonly IReadOnlyDictionary<string, string>? _uniqueByTaskType;

        public FakeCompletionMetadataResolver(
            IReadOnlyDictionary<(string, string), string> byPair,
            IReadOnlyDictionary<string, string>? uniqueByTaskType = null)
        {
            _byPair = byPair;
            _uniqueByTaskType = uniqueByTaskType;
        }

        public int CallCount { get; private set; }

        public string? ResolveCompletionEventCode(string taskTypeCode, string? taskResultCode)
        {
            CallCount++;

            if (string.IsNullOrWhiteSpace(taskTypeCode))
                return null;

            if (string.IsNullOrWhiteSpace(taskResultCode))
                return _uniqueByTaskType is not null
                    && _uniqueByTaskType.TryGetValue(taskTypeCode, out var unique)
                    ? unique
                    : null;

            return _byPair.TryGetValue((taskTypeCode, taskResultCode!), out var ev) ? ev : null;
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

        public Task<InspectionReportDetail?> GetReportDetailAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<InspectionReportDetail?>(null);

        public Task<IReadOnlyList<InspectionChapterNode>> GetQuestionnaireTreeAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionChapterNode>>([]);

        public Task<IReadOnlyList<InspectionDrawingRow>> GetDrawingsAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionDrawingRow>>([]);

        public Task<IReadOnlyList<InspectionReviewedFileRow>> GetReviewedFilesAsync(
            int reportId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InspectionReviewedFileRow>>([]);
    }
}
