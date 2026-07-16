namespace SiNet.Application.ProjectWork;

/// <summary>
/// Default <see cref="IActiveFileQueryHub"/> — a thin, thread-safe indirection over the currently
/// registered provider (the loaded ProjectWork surface). Pure Application code, registered as a
/// singleton so unrelated surfaces resolve the same instance.
/// </summary>
public sealed class ActiveFileQueryHub : IActiveFileQueryHub
{
    private volatile IActiveFileQueryService? _provider;

    /// <inheritdoc />
    public event Action<IActiveFileQueryService?>? ProviderChanged;

    /// <inheritdoc />
    public void RegisterProvider(IActiveFileQueryService? provider)
    {
        _provider = provider;
        ProviderChanged?.Invoke(provider);
    }

    /// <inheritdoc />
    public void UnregisterProvider(IActiveFileQueryService provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!ReferenceEquals(_provider, provider))
            return;
        RegisterProvider(null);
    }

    /// <inheritdoc />
    public void NotifyAvailabilityChanged() => ProviderChanged?.Invoke(_provider);

    /// <inheritdoc />
    public bool IsAvailable => _provider?.IsAvailable == true;

    /// <inheritdoc />
    public int? CurrentProjectNumber => _provider?.CurrentProjectNumber;

    /// <inheritdoc />
    public IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(int folderId)
        => _provider?.GetActiveFilesInFolder(folderId) ?? Array.Empty<ActiveFileInfo>();

    /// <inheritdoc />
    public IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(string folderFullPath)
        => _provider?.GetActiveFilesInFolder(folderFullPath) ?? Array.Empty<ActiveFileInfo>();

    /// <inheritdoc />
    public IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(int folderId, bool recursive)
        => _provider?.GetActiveFilesInFolder(folderId, recursive) ?? Array.Empty<ActiveFileInfo>();

    /// <inheritdoc />
    public ActiveFileInfo? FindActiveFileByName(string fileName)
        => _provider?.FindActiveFileByName(fileName);

    /// <inheritdoc />
    public IReadOnlyList<ActiveFileInfo> GetAllActiveFiles()
        => _provider?.GetAllActiveFiles() ?? Array.Empty<ActiveFileInfo>();

    /// <inheritdoc />
    public IReadOnlyList<ActiveFolderInfo> GetActiveFolderTree()
        => _provider?.GetActiveFolderTree() ?? Array.Empty<ActiveFolderInfo>();
}
