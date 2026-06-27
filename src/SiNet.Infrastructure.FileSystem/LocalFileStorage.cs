using SiNet.Application.Abstractions.FileSystem;

namespace SiNet.Infrastructure.FileSystem;

/// <summary>
/// <see cref="IFileStorage"/> backed by the local file system. Self-contained (no legacy
/// dependencies), so it is wired as a real implementation already in the Foundation Round.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(path));

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(File.OpenRead(path));

    public async Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var target = File.Create(path);
        await content.CopyToAsync(target, cancellationToken);
    }
}
