namespace SiNet.Application.Projects;

/// <summary>
/// Event payload raised by <see cref="ICurrentProjectContext.CurrentProjectChanged"/> when the shell's
/// Current Project changes (see <c>docs/PROJECTS.md</c> §4/§11).
/// <para>
/// <see cref="Project"/> is the newly selected project, or <see langword="null"/> when the current
/// project was cleared. Subscribers (e.g. the MainWindow/MainShell title, the Email header) react to
/// this; they must treat <see langword="null"/> as "no project selected" and never guess a project.
/// </para>
/// </summary>
public sealed class ProjectChangedEventArgs : EventArgs
{
    /// <summary>Creates the event args for a current-project change.</summary>
    /// <param name="project">The newly selected project, or <see langword="null"/> when cleared.</param>
    public ProjectChangedEventArgs(ProjectSummaryDto? project) => Project = project;

    /// <summary>The newly selected current project, or <see langword="null"/> when cleared.</summary>
    public ProjectSummaryDto? Project { get; }
}
