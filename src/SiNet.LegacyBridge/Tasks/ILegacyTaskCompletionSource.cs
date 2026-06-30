namespace SiNet.LegacyBridge.Tasks;

/// <summary>
/// Legacy-host seam over the legacy <c>TaskCompletionCoordinator</c>. Mirrors only the single
/// completion path the new vertical slice needs, expressed in bridge-local DTOs so this assembly has
/// no dependency on <c>SiNetSQL</c>.
/// <para>
/// The new app host (<c>SiNet.App.Wpf</c>) leaves this seam unbound, so
/// <see cref="LegacyTaskCompletionService"/> reports an <c>Unavailable</c> result (the slice can still
/// navigate and load read-only). The legacy WPF host binds a concrete implementation that calls
/// <c>TaskCompletionCoordinator.CompleteAsync</c> — which itself routes workflow auto-advance through
/// the official <c>IWorkflowCommandService.CheckAndAutoAdvanceAsync</c> — and projects its result into
/// <see cref="LegacyTaskCompletionResultDto"/>. Remove this seam once a native infrastructure
/// task-completion service replaces it.
/// </para>
/// </summary>
public interface ILegacyTaskCompletionSource
{
    /// <summary>
    /// Completes the task described by <paramref name="command"/> and returns the legacy outcome,
    /// including any workflow auto-advance result. Implementations should return a non-successful
    /// DTO (not throw) for ordinary validation/business failures.
    /// </summary>
    ValueTask<LegacyTaskCompletionResultDto> CompleteAsync(
        LegacyCompleteTaskCommandDto command,
        CancellationToken cancellationToken = default);
}
