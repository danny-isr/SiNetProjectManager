namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Clean seam for item-level ACC operations that are needed by file-tree and replace flows without
/// exposing the legacy connector directly to callers.
/// </summary>
public interface IAccItemService
{
    Task<string?> GetDisplayNameAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tip version URN for an item lineage (Data Management <c>…/items/{itemId}/tip</c> → <c>data.id</c>).
    /// </summary>
    Task<string?> GetTipVersionIdAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);

    Task<int?> GetVersionCountAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);

    Task<bool> HideAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default);
}
