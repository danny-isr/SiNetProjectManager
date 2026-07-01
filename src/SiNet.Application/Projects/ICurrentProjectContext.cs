namespace SiNet.Application.Projects;

/// <summary>
/// Runtime-only port holding the application's <b>Current Project</b> — the project the user has
/// selected for manual navigation and shared shell context (see <c>docs/PROJECTS.md</c> §4).
/// <para>
/// Rules (authoritative in <c>docs/PROJECTS.md</c>):
/// </para>
/// <list type="bullet">
///   <item>It is <b>runtime-only</b> and is <b>not persisted</b> unless a future requirement says so.</item>
///   <item><see cref="CurrentProject"/> <b>may be <see langword="null"/></b> (no project selected); it must never be invented or guessed.</item>
///   <item>Setting a project raises <see cref="CurrentProjectChanged"/>, but only when the value actually
///   changes — implementations <b>de-duplicate by <c>ProjectId</c></b> (setting the same id again is a no-op).</item>
///   <item>It is for <b>manual navigation / shell context</b> only. It must <b>not</b> silently override an
///   explicit <c>WorkSurfaceContext.ProjectId</c> on a task-opened surface (see <c>docs/PROJECTS.md</c> §7).</item>
/// </list>
/// <para>
/// The legacy analog is <c>SiNetSQL.Services.ActiveProjectContext</c>; this port formalizes that concept
/// behind the Application layer. This slice binds an in-memory implementation.
/// </para>
/// </summary>
public interface ICurrentProjectContext
{
    /// <summary>
    /// The project currently selected for manual navigation, or <see langword="null"/> when none is
    /// selected. Callers must treat <see langword="null"/> as "none" and never substitute a project.
    /// </summary>
    ProjectSummaryDto? CurrentProject { get; }

    /// <summary>
    /// Raised when <see cref="CurrentProject"/> changes to a different project (or to/from
    /// <see langword="null"/>). Not raised when the same <c>ProjectId</c> is set again. May be raised
    /// off the UI thread — WPF subscribers marshal to the dispatcher.
    /// </summary>
    event EventHandler<ProjectChangedEventArgs>? CurrentProjectChanged;

    /// <summary>
    /// Sets the current project (or clears it with <see langword="null"/>). De-duplicates by
    /// <c>ProjectId</c>: if the incoming project has the same id as the current one, no change occurs
    /// and no event is raised. Does not mutate workflow or complete tasks — it only updates shell state.
    /// </summary>
    /// <param name="project">The project to make current, or <see langword="null"/> to clear.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task SetCurrentProjectAsync(
        ProjectSummaryDto? project,
        CancellationToken cancellationToken = default);
}
