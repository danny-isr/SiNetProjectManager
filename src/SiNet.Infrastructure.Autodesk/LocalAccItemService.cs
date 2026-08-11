using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class LocalAccItemService(IAccTransferConnector connector) : IAccItemService
{
    private readonly IAccTransferConnector _connector = connector;

    public Task<string?> GetDisplayNameAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(itemId)
            ? Task.FromResult<string?>(null)
            : _connector.GetItemDisplayNameAsync(NormalizeProjectId(projectId), itemId.Trim(), cancellationToken);

    public Task<string?> GetTipVersionIdAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(itemId)
            ? Task.FromResult<string?>(null)
            : _connector.GetItemTipVersionIdAsync(NormalizeProjectId(projectId), itemId.Trim(), cancellationToken);

    public Task<int?> GetVersionCountAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(itemId)
            ? Task.FromResult<int?>(null)
            : _connector.GetItemVersionCountAsync(NormalizeProjectId(projectId), itemId.Trim(), cancellationToken);

    public Task<bool> HideAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(itemId)
            ? Task.FromResult(false)
            : _connector.HideItemAsync(NormalizeProjectId(projectId), itemId.Trim(), cancellationToken);

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
}
