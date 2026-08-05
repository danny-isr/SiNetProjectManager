namespace SiNet.Application.Projects;

/// <summary>
/// Centralized rename: FileServer → ACC Docs → Drive → DB Title last. Gmail is out of scope.
/// </summary>
public interface IProjectRenameOrchestrator
{
    Task<ProjectRenameAnalysis> AnalyzeAsync(
        int projectId,
        string newTitle,
        CancellationToken cancellationToken = default);

    Task<ProjectRenameExecuteResult> ExecuteAsync(
        ProjectRenameAnalysis analysis,
        CancellationToken cancellationToken = default);
}
