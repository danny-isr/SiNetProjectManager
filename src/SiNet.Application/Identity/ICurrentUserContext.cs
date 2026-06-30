namespace SiNet.Application.Identity;

/// <summary>
/// Runtime-only port exposing the <b>currently authenticated host user</b> to feature screens that
/// need to attribute an action to a real user (e.g. recording who completed a task) without inventing
/// an id.
/// <para>
/// This is intentionally minimal: it carries only the application user id. It is <b>not</b> persisted
/// and is never an authority for authorization decisions — those stay in the host. The legacy WPF host
/// (<c>SiNetProjectManagerV2</c>) binds an adapter backed by its authenticated
/// <c>CurrentUserContext</c>; hosts that have no authenticated user (e.g. the early
/// <c>SiNet.App.Wpf</c> preview harness) simply leave it unbound, in which case
/// <see cref="UserId"/> is <see langword="null"/> and the caller must fall back to an explicit input
/// rather than guess.
/// </para>
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>
    /// The current application user id, or <see langword="null"/> when no authenticated user is
    /// available in this host. Callers must treat <see langword="null"/> as "unknown" and never
    /// substitute an arbitrary id.
    /// </summary>
    int? UserId { get; }
}
