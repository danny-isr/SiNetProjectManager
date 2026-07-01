using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// In-memory <see cref="ICurrentProjectContext"/> for the first Project Context slice
/// (see <c>docs/PROJECTS.md</c> §4 and <c>docs/PROJECT_CONTEXT_MIGRATION.md</c>).
/// <para>
/// It is the runtime holder of the shell's Current Project. It is <b>not persisted</b>, may be
/// <see langword="null"/>, is thread-safe for reads/writes, and — mirroring the legacy
/// <c>SiNetSQL.Services.ActiveProjectContext</c> — <b>de-duplicates by <c>ProjectId</c></b>: setting the
/// same project id again is a no-op and does not raise <see cref="CurrentProjectChanged"/>. It never
/// mutates workflow or completes tasks; it only updates and broadcasts shell state.
/// </para>
/// </summary>
public sealed class InMemoryCurrentProjectContext : ICurrentProjectContext
{
    private readonly object _gate = new();
    private ProjectSummaryDto? _currentProject;

    /// <inheritdoc />
    public ProjectSummaryDto? CurrentProject
    {
        get
        {
            lock (_gate)
            {
                return _currentProject;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<ProjectChangedEventArgs>? CurrentProjectChanged;

    /// <inheritdoc />
    public Task SetCurrentProjectAsync(
        ProjectSummaryDto? project,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool changed;
        lock (_gate)
        {
            // De-dupe by ProjectId (and by null<->set transitions). No change => no event.
            changed = !SameProject(_currentProject, project);
            if (changed)
            {
                _currentProject = project;
            }
        }

        if (changed)
        {
            CurrentProjectChanged?.Invoke(this, new ProjectChangedEventArgs(project));
        }

        return Task.CompletedTask;
    }

    private static bool SameProject(ProjectSummaryDto? a, ProjectSummaryDto? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.ProjectId == b.ProjectId;
    }
}
