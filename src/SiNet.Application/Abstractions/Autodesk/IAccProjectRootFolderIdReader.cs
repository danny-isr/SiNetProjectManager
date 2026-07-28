namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Reads the Autodesk Docs root folder id of a project directly from the ACC API.
/// </summary>
/// <remarks>
/// Split out of <see cref="IAccProjectRootFolderResolver"/> so the SQL layer can resolve the hub
/// from the database without referencing the Autodesk SDK. The SQL resolver owns the hub lookup and
/// delegates the remote call to this port; the implementation lives in the Autodesk module.
/// </remarks>
public interface IAccProjectRootFolderIdReader
{
    Task<string?> GetProjectRootFolderIdAsync(
        string hubId,
        string projectId,
        CancellationToken cancellationToken = default);
}
