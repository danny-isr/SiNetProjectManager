namespace SiNet.Application.ProjectWork;

/// <summary>
/// Unified port for opening a project file/version, deciding between the in-app ACC viewer and the
/// local desktop application based on (in order): an explicit <c>ForceOpenWith</c>, the per-file
/// <c>OpenWith</c> sidecar override, then the file's storage destination default. Clean-layer port of
/// the legacy <c>SiNetSQL.Services.FileOpen.IFileOpenService</c>.
/// <para>
/// Implemented by the loaded ProjectWork surface and exposed process-wide through
/// <see cref="IFileOpenHub"/> so other surfaces can open files from the current tree.
/// </para>
/// </summary>
public interface IFileOpenService
{
    /// <summary>True when a project tree is loaded and open requests can be served.</summary>
    bool IsAvailable { get; }

    /// <summary>Resolves the request and opens the file. Safe to call from any thread.</summary>
    Task<FileOpenResult> OpenAsync(FileOpenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a per-file open-location preference across every existing version's sidecar. Pass
    /// <see langword="null"/> for <paramref name="openWith"/> to clear the override.
    /// </summary>
    Task<bool> SetOpenPreferenceAsync(int fileId, string? openWith, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-version override: writes the preference into the sidecar of a single physical file. Pass
    /// <see langword="null"/> for <paramref name="openWith"/> to clear.
    /// </summary>
    Task<bool> SetOpenPreferenceForPathAsync(string fullPath, string? openWith, CancellationToken cancellationToken = default);
}

/// <summary>
/// Process-wide hub for resolving the active <see cref="IFileOpenService"/> provider (the loaded
/// ProjectWork surface). Clean-layer port of the legacy <c>FileOpenServiceRegistry</c> singleton.
/// </summary>
public interface IFileOpenHub : IFileOpenService
{
    /// <summary>Registers the live provider. Passing <see langword="null"/> unregisters the current one.</summary>
    void RegisterProvider(IFileOpenService? provider);
}
