namespace SiNet.Application.Tasks;

/// <summary>
/// Runtime-only port that resolves the canonical <b>completion event code</b> for a task from the
/// <c>(task type, selected result)</c> pair, reusing the host's existing declarative completion-behavior
/// mapping. It exists so feature screens can complete branching tasks — whose result selects between
/// several events (e.g. <c>RecheckPlan</c> → <c>ReviewRecheckPassed</c> /
/// <c>ReviewRecheckRequiresMoreCorrections</c>) — <b>without owning any mapping table and without
/// guessing</b>.
/// <para>
/// The resolver is read-only and carries no authority over workflow: it only translates well-known
/// codes. Callers still complete through <see cref="ITaskCompletionService"/>. A <see langword="null"/>
/// result means the pair is unknown, unsupported, or ambiguous, and the caller MUST block completion
/// rather than substitute an arbitrary event code. Hosts that cannot supply the mapping (e.g. the early
/// <c>SiNet.App.Wpf</c> preview harness) simply leave this port unbound, in which case the caller falls
/// back to an explicit input.
/// </para>
/// </summary>
public interface ITaskCompletionMetadataResolver
{
    /// <summary>
    /// Returns the unique completion event code for <paramref name="taskTypeCode"/> and the selected
    /// <paramref name="taskResultCode"/>, or <see langword="null"/> when no single event applies.
    /// <para>
    /// When <paramref name="taskResultCode"/> is <see langword="null"/>/whitespace, implementations
    /// resolve only when the task type maps to exactly one event (the unambiguous case); otherwise they
    /// resolve only when exactly one event allows that exact pair. In every other situation — missing,
    /// invalid, or ambiguous — they return <see langword="null"/>. Pure: it never mutates state.
    /// </para>
    /// </summary>
    string? ResolveCompletionEventCode(string taskTypeCode, string? taskResultCode);
}
