namespace SiNet.Application.Abstractions.FileSystem;

/// <summary>
/// Abstraction over local/remote file persistence and IO. Implemented by
/// <c>SiNet.Infrastructure.FileSystem</c>.
/// </summary>
public interface IFileStorage
{
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default);
}
