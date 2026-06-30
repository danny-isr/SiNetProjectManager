using SiNet.Application.WorkSurfaces;
using SiNet.LegacyBridge.Tasks;
using Xunit;

namespace SiNet.LegacyBridge.Tests;

/// <summary>
/// Unit tests for the strangler adapter <see cref="LegacyTaskNavigationService"/> that implements
/// the Application <c>ITaskNavigationService</c> port over the optional
/// <see cref="ILegacyTaskNavigationSource"/> seam. These lock in the workflow-first navigation
/// guarantees: no source -> null, resolver failure -> null (no guessed target), faithful mapping to
/// <see cref="WorkSurfaceContext"/>, and the long->int work-target id guard (no silent truncation).
/// </summary>
public sealed class LegacyTaskNavigationServiceTests
{
    [Fact]
    public async Task ResolveAsync_returns_null_when_source_is_unbound()
    {
        // New app host leaves the seam unbound: the surface must show a clear "cannot open from
        // task yet" message rather than guess a target, so the context must be null.
        var sut = new LegacyTaskNavigationService(source: null);

        var context = await sut.ResolveAsync(taskId: 42, ct: CancellationToken.None);

        Assert.Null(context);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_when_legacy_result_is_failure()
    {
        // Case 2 (task does not resolve / cannot be opened): IsSuccess=false must collapse to a null
        // context with no fallback to an arbitrary work target.
        var source = new FakeSource(new LegacyTaskNavigationRequestDto(
            TaskId: 7,
            ProjectId: 100,
            WorkflowInstanceId: 5,
            ComponentKey: "Inspection",
            PrimaryWorkTargetEntityId: 999,
            AllowedTaskResultCodes: new[] { "Approved" },
            IsSuccess: false,
            FailureMessage: "no interaction definition"));
        var sut = new LegacyTaskNavigationService(source);

        var context = await sut.ResolveAsync(taskId: 7, ct: CancellationToken.None);

        Assert.Null(context);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_when_source_returns_null()
    {
        var sut = new LegacyTaskNavigationService(new FakeSource(result: null));

        var context = await sut.ResolveAsync(taskId: 7, ct: CancellationToken.None);

        Assert.Null(context);
    }

    [Fact]
    public async Task ResolveAsync_maps_successful_result_to_work_surface_context()
    {
        // Case 1 + Case 3 support: a successful result is mapped faithfully (including the
        // ComponentKey the shell uses to accept/reject) and the exact work-target id is preserved.
        var source = new FakeSource(new LegacyTaskNavigationRequestDto(
            TaskId: 11,
            ProjectId: 250,
            WorkflowInstanceId: 9,
            ComponentKey: "Inspection",
            PrimaryWorkTargetEntityId: 3030,
            AllowedTaskResultCodes: new[] { "Approved", "Rejected" },
            IsSuccess: true,
            FailureMessage: null));
        var sut = new LegacyTaskNavigationService(source);

        var context = await sut.ResolveAsync(taskId: 11, ct: CancellationToken.None);

        Assert.NotNull(context);
        Assert.Equal(11, context!.TaskId);
        Assert.Equal(250, context.ProjectId);
        Assert.Equal(9, context.WorkflowInstanceId);
        Assert.Equal("Inspection", context.ComponentKey);
        Assert.Equal(3030, context.PrimaryWorkTargetEntityId);
        Assert.Equal(new[] { "Approved", "Rejected" }, context.AllowedResultCodes);
    }

    [Fact]
    public async Task ResolveAsync_maps_null_project_to_zero()
    {
        // Project-independent tasks have a null ProjectId in the legacy DTO; the context exposes a
        // non-nullable ProjectId, so it must be normalized to 0 (not throw).
        var source = new FakeSource(new LegacyTaskNavigationRequestDto(
            TaskId: 12,
            ProjectId: null,
            WorkflowInstanceId: null,
            ComponentKey: "Inspection",
            PrimaryWorkTargetEntityId: 1,
            AllowedTaskResultCodes: Array.Empty<string>(),
            IsSuccess: true,
            FailureMessage: null));
        var sut = new LegacyTaskNavigationService(source);

        var context = await sut.ResolveAsync(taskId: 12, ct: CancellationToken.None);

        Assert.NotNull(context);
        Assert.Equal(0, context!.ProjectId);
    }

    [Fact]
    public async Task ResolveAsync_maps_null_work_target_to_null()
    {
        // Case 4 (Inspection task with no concrete report target): the null work-target id must flow
        // through as null so the shell can reject it without inventing a report.
        var source = new FakeSource(new LegacyTaskNavigationRequestDto(
            TaskId: 13,
            ProjectId: 250,
            WorkflowInstanceId: 9,
            ComponentKey: "Inspection",
            PrimaryWorkTargetEntityId: null,
            AllowedTaskResultCodes: Array.Empty<string>(),
            IsSuccess: true,
            FailureMessage: null));
        var sut = new LegacyTaskNavigationService(source);

        var context = await sut.ResolveAsync(taskId: 13, ct: CancellationToken.None);

        Assert.NotNull(context);
        Assert.Null(context!.PrimaryWorkTargetEntityId);
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData((long)int.MaxValue + 1)]
    [InlineData((long)int.MinValue - 1)]
    public async Task ResolveAsync_does_not_truncate_out_of_int_range_work_target_id(long outOfRange)
    {
        // A long work-target id outside int range must NOT be silently truncated into a different
        // valid int id (which could select the wrong report). It maps to "no concrete target".
        var source = new FakeSource(new LegacyTaskNavigationRequestDto(
            TaskId: 14,
            ProjectId: 250,
            WorkflowInstanceId: 9,
            ComponentKey: "Inspection",
            PrimaryWorkTargetEntityId: outOfRange,
            AllowedTaskResultCodes: Array.Empty<string>(),
            IsSuccess: true,
            FailureMessage: null));
        var sut = new LegacyTaskNavigationService(source);

        var context = await sut.ResolveAsync(taskId: 14, ct: CancellationToken.None);

        Assert.NotNull(context);
        Assert.Null(context!.PrimaryWorkTargetEntityId);
    }

    [Theory]
    [InlineData((long)int.MaxValue)]
    [InlineData((long)int.MinValue)]
    [InlineData(5050L)]
    public async Task ResolveAsync_preserves_in_range_work_target_id(long inRange)
    {
        var source = new FakeSource(new LegacyTaskNavigationRequestDto(
            TaskId: 15,
            ProjectId: 250,
            WorkflowInstanceId: 9,
            ComponentKey: "Inspection",
            PrimaryWorkTargetEntityId: inRange,
            AllowedTaskResultCodes: Array.Empty<string>(),
            IsSuccess: true,
            FailureMessage: null));
        var sut = new LegacyTaskNavigationService(source);

        var context = await sut.ResolveAsync(taskId: 15, ct: CancellationToken.None);

        Assert.NotNull(context);
        Assert.Equal((int)inRange, context!.PrimaryWorkTargetEntityId);
    }

    private sealed class FakeSource : ILegacyTaskNavigationSource
    {
        private readonly LegacyTaskNavigationRequestDto? _result;

        public FakeSource(LegacyTaskNavigationRequestDto? result) => _result = result;

        public ValueTask<LegacyTaskNavigationRequestDto?> ResolveAsync(
            int taskId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_result);
    }
}
