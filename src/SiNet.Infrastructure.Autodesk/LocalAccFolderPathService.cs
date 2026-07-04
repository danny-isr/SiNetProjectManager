using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class LocalAccFolderPathService(IAccTransferConnector connector) : IAccFolderPathService
{
    private readonly IAccTransferConnector _connector = connector;

    public async Task<string?> TryResolvePathAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(rootFolderId))
        {
            return null;
        }

        var normalizedProjectId = NormalizeProjectId(projectId);
        var currentFolderId = rootFolderId.Trim();
        foreach (var segment in NormalizePathSegments(pathSegments))
        {
            currentFolderId = await _connector
                .GetFolderByNameAsync(normalizedProjectId, currentFolderId, segment, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(currentFolderId))
            {
                return null;
            }
        }

        return currentFolderId;
    }

    public Task<string> EnsurePathAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("ACC project id is required.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(rootFolderId))
            throw new ArgumentException("ACC root folder id is required.", nameof(rootFolderId));

        return _connector.EnsureFolderPathAsync(
            NormalizeProjectId(projectId),
            rootFolderId.Trim(),
            NormalizePathSegments(pathSegments),
            cancellationToken);
    }

    private static string NormalizeProjectId(string projectId)
    {
        var trimmed = projectId.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"b.{trimmed}";
    }

    private static IReadOnlyList<string> NormalizePathSegments(IReadOnlyList<string> pathSegments) =>
        pathSegments
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .Select(static segment => segment.Trim())
            .ToArray();
}
