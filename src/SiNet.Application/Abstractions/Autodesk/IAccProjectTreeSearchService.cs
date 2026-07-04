namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Read-only ACC project tree search. Searches files by name from the supplied folder or from
/// the project's "Project Files" root when no folder ID is provided.
/// </summary>
public interface IAccProjectTreeSearchService
{
    Task<AccProjectTreeSearchResult> SearchAsync(
        string projectId,
        string fileName,
        string? folderId = null,
        CancellationToken cancellationToken = default);
}
