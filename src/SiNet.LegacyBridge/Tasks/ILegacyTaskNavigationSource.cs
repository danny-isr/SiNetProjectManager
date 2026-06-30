namespace SiNet.LegacyBridge.Tasks;

/// <summary>
/// Legacy-host seam over the legacy <c>TaskNavigationResolver</c>. Mirrors only the single resolve
/// the new Work Surface navigation needs, expressed in terms of the bridge-local
/// <see cref="LegacyTaskNavigationRequestDto"/> so this assembly has no dependency on <c>SiNetSQL</c>.
/// <para>
/// The new app host (<c>SiNet.App.Wpf</c>) leaves this seam unbound, so
/// <see cref="LegacyTaskNavigationService"/> degrades to a <see langword="null"/> context. The legacy
/// WPF host (<c>SiNetProjectManagerV2</c>) — which already references both worlds — binds a concrete
/// implementation that calls <c>TaskNavigationResolver.ResolveAsync</c> and projects its
/// <c>TaskNavigationRequest</c> into <see cref="LegacyTaskNavigationRequestDto"/>. Remove this seam
/// once a native infrastructure task-navigation source replaces it.
/// </para>
/// </summary>
public interface ILegacyTaskNavigationSource
{
    /// <summary>
    /// Resolves how <paramref name="taskId"/> should be opened, or <see langword="null"/> when the
    /// task does not exist. Implementations should return a DTO with
    /// <see cref="LegacyTaskNavigationRequestDto.IsSuccess"/> = false (not throw) when the task
    /// exists but cannot be opened.
    /// </summary>
    ValueTask<LegacyTaskNavigationRequestDto?> ResolveAsync(
        int taskId,
        CancellationToken cancellationToken = default);
}
