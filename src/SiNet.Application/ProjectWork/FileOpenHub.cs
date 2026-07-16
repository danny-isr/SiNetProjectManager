namespace SiNet.Application.ProjectWork;

/// <summary>
/// Default <see cref="IFileOpenHub"/> — a thin indirection over the currently registered
/// <see cref="IFileOpenService"/> provider (the loaded ProjectWork surface). Returns an
/// <see cref="FileOpenOutcome.Unavailable"/> result when no provider is registered.
/// </summary>
public sealed class FileOpenHub : IFileOpenHub
{
    private volatile IFileOpenService? _provider;

    /// <inheritdoc />
    public void RegisterProvider(IFileOpenService? provider) => _provider = provider;

    /// <inheritdoc />
    public void UnregisterProvider(IFileOpenService provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!ReferenceEquals(_provider, provider))
            return;
        _provider = null;
    }

    /// <inheritdoc />
    public bool IsAvailable => _provider?.IsAvailable == true;

    /// <inheritdoc />
    public Task<FileOpenResult> OpenAsync(FileOpenRequest request, CancellationToken cancellationToken = default)
        => _provider is { } p
            ? p.OpenAsync(request, cancellationToken)
            : Task.FromResult(new FileOpenResult(FileOpenOutcome.Unavailable));

    /// <inheritdoc />
    public Task<bool> SetOpenPreferenceAsync(int fileId, string? openWith, CancellationToken cancellationToken = default)
        => _provider is { } p
            ? p.SetOpenPreferenceAsync(fileId, openWith, cancellationToken)
            : Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> SetOpenPreferenceForPathAsync(string fullPath, string? openWith, CancellationToken cancellationToken = default)
        => _provider is { } p
            ? p.SetOpenPreferenceForPathAsync(fullPath, openWith, cancellationToken)
            : Task.FromResult(false);
}
