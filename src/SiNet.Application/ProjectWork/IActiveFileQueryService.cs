namespace SiNet.Application.ProjectWork;

/// <summary>
/// Lookup port exposing the "active" files (with alternatives and versions) from the in-memory project
/// tree maintained by the loaded ProjectWork surface. Other surfaces (Inspection, Email) consume this
/// without depending on WPF tree-node types. Clean-layer port of the legacy
/// <c>SiNetSQL.Services.ActiveFileQuery.IActiveFileQueryService</c>.
/// <para>
/// The ProjectWork surface registers itself as the provider through <see cref="IActiveFileQueryHub"/>.
/// </para>
/// </summary>
public interface IActiveFileQueryService
{
    /// <summary>True when a project tree is currently loaded and queryable.</summary>
    bool IsAvailable { get; }

    /// <summary>The project number whose tree is currently loaded, if any.</summary>
    int? CurrentProjectNumber { get; }

    /// <summary>Active files directly under the folder identified by <paramref name="folderId"/> (non-recursive).</summary>
    IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(int folderId);

    /// <summary>Active files directly under the folder identified by its full path (non-recursive).</summary>
    IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(string folderFullPath);

    /// <summary>Active files under the folder, optionally walking into descendant subfolders.</summary>
    IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(int folderId, bool recursive);

    /// <summary>Resolves a logical file by name across the entire active project tree, or <see langword="null"/>.</summary>
    ActiveFileInfo? FindActiveFileByName(string fileName) => null;

    /// <summary>Every active file currently visible in the loaded project tree.</summary>
    IReadOnlyList<ActiveFileInfo> GetAllActiveFiles() => Array.Empty<ActiveFileInfo>();

    /// <summary>The active project tree as nested folder snapshots.</summary>
    IReadOnlyList<ActiveFolderInfo> GetActiveFolderTree() => Array.Empty<ActiveFolderInfo>();
}

/// <summary>
/// Process-wide hub for resolving the active <see cref="IActiveFileQueryService"/> provider. The loaded
/// ProjectWork surface registers itself here so other surfaces (which hold only the singleton) can query
/// files without coupling to the surface view-model directly. Clean-layer port of the legacy
/// <c>ActiveFileQueryRegistry</c> singleton, now a DI-registered service.
/// </summary>
public interface IActiveFileQueryHub : IActiveFileQueryService
{
    /// <summary>Registers the live provider. Passing <see langword="null"/> unregisters the current one.</summary>
    void RegisterProvider(IActiveFileQueryService? provider);

    /// <summary>Re-raises <see cref="ProviderChanged"/> so subscribers can refresh availability.</summary>
    void NotifyAvailabilityChanged();

    /// <summary>Raised when the underlying provider is registered, replaced, or its availability flips.</summary>
    event Action<IActiveFileQueryService?>? ProviderChanged;
}
