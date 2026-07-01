using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// Pure, UI-free helper that renders the application shell title from the Current Project
/// (see <c>docs/PROJECTS.md</c> §11 "Application title/header behavior").
/// <para>
/// The shell is the <b>single</b> place that reflects the Current Project in the app title/header;
/// individual feature windows must not set the global title. This formatter centralizes that string so
/// the rule "null project ⇒ default title" and the format "<c>{default} - {ProjectName}</c>" have one
/// testable source of truth, shared by whichever context feeds the title (the new
/// <see cref="ICurrentProjectContext"/> and, temporarily, the legacy <c>ActiveProjectContext</c>).
/// </para>
/// <para>
/// It has no dependency on WPF, the DB, or any project source; it only transforms a default title plus
/// an optional <see cref="ProjectSummaryDto"/> into a display string.
/// </para>
/// </summary>
public static class ProjectTitleFormatter
{
    /// <summary>
    /// Builds the shell title for the given <paramref name="defaultTitle"/> and Current Project.
    /// <list type="bullet">
    ///   <item><description><paramref name="project"/> is <see langword="null"/> ⇒ returns <paramref name="defaultTitle"/> unchanged.</description></item>
    ///   <item><description><paramref name="project"/> has a non-blank <see cref="ProjectSummaryDto.ProjectName"/> ⇒ returns <c>"{defaultTitle} - {ProjectName}"</c>.</description></item>
    ///   <item><description><paramref name="project"/> has a blank/whitespace name ⇒ returns <paramref name="defaultTitle"/> (never appends an empty " - ").</description></item>
    /// </list>
    /// </summary>
    /// <param name="defaultTitle">The application's default title (e.g. <c>"תוכנת ניהול   v1.2.3"</c>).</param>
    /// <param name="project">The Current Project, or <see langword="null"/> when none is selected.</param>
    /// <returns>The formatted shell title.</returns>
    public static string Format(string defaultTitle, ProjectSummaryDto? project)
        => Format(defaultTitle, project?.ProjectName);

    /// <summary>
    /// Overload that formats from a raw project name, so callers that only have a name (e.g. the legacy
    /// shell handling <c>ActiveProjectContext</c>'s project) share the same format and default rules.
    /// A <see langword="null"/> or blank name returns <paramref name="defaultTitle"/> unchanged.
    /// </summary>
    /// <param name="defaultTitle">The application's default title.</param>
    /// <param name="projectName">The Current Project's display name, or <see langword="null"/>/blank for none.</param>
    /// <returns>The formatted shell title.</returns>
    public static string Format(string defaultTitle, string? projectName)
    {
        ArgumentNullException.ThrowIfNull(defaultTitle);

        if (string.IsNullOrWhiteSpace(projectName))
        {
            return defaultTitle;
        }

        return $"{defaultTitle} - {projectName}";
    }
}
