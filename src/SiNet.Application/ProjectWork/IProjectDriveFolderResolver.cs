namespace SiNet.Application.ProjectWork;

/// <summary>
/// Resolves the relative Drive folder path segments for a project folder (DB walk only — no Drive
/// API). The first segment is the project root name under <c>ProjectsRootFolderId</c>; subsequent
/// segments mirror the <c>ProjectFolder</c> hierarchy (excluding the synthetic project-root folder).
/// </summary>
public interface IProjectDriveFolderResolver
{
    /// <summary>
    /// Returns relative path segments under the configured projects-root Drive folder, or
    /// <see langword="null"/> when the project/folder cannot be resolved.
    /// </summary>
    Task<IReadOnlyList<string>?> ResolveRelativeSegmentsAsync(
        int projectId,
        int projectFolderId,
        CancellationToken cancellationToken = default);
}
