namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Resolves or ensures an ACC folder lineage beneath a known root folder. This is intentionally
/// separate from upload so callers can obtain concrete folder ids before listing, deduping, or
/// persisting ACC folder references.
/// </summary>
public interface IAccFolderPathService
{
    Task<string?> TryResolvePathAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        CancellationToken cancellationToken = default);

    Task<string> EnsurePathAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        CancellationToken cancellationToken = default);
}
