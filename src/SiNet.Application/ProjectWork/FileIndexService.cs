using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SiNet.Domain.Files;

namespace SiNet.Application.ProjectWork;

/// <summary>
/// Default <see cref="IFileIndexService"/> implementation. Pure orchestration over the registered
/// <see cref="IFileStore"/> set — no external IO of its own — so it lives in the Application layer.
/// </summary>
public sealed class FileIndexService : IFileIndexService
{
    private readonly IReadOnlyList<IFileStore> _stores;
    private readonly ConcurrentDictionary<(int projectId, string fileName, FileStorageDestination dest), byte> _inFlight = new();

    public FileIndexService(IEnumerable<IFileStore> stores)
    {
        _stores = stores?.ToList() ?? new List<IFileStore>();
    }

    /// <inheritdoc />
    public event Action<InFlightChange>? InFlightChanged;

    /// <inheritdoc />
    public IReadOnlyList<FileStorageDestination> AvailableDestinations
        => _stores.Select(s => s.Destination).Distinct().ToList();

    /// <inheritdoc />
    public IFileStore? GetStore(FileStorageDestination destination)
        => _stores.FirstOrDefault(s => s.Destination == destination);

    /// <inheritdoc />
    public void MarkInFlight(int projectId, string fileName, FileStorageDestination destination)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;
        _inFlight[(projectId, fileName, destination)] = 1;
        InFlightChanged?.Invoke(new InFlightChange(projectId, fileName, destination, true));
    }

    /// <inheritdoc />
    public void ClearInFlight(int projectId, string fileName, FileStorageDestination destination)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;
        if (_inFlight.TryRemove((projectId, fileName, destination), out _))
            InFlightChanged?.Invoke(new InFlightChange(projectId, fileName, destination, false));
    }

    /// <inheritdoc />
    public bool IsInFlight(int projectId, string fileName, FileStorageDestination destination)
        => _inFlight.ContainsKey((projectId, fileName, destination));

    /// <inheritdoc />
    public async IAsyncEnumerable<ScannedFile> ScanFolderAsync(
        int projectId,
        int projectFolderId,
        IEnumerable<FileStorageDestination> destinations,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<ScannedFile>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var wanted = new HashSet<FileStorageDestination>(destinations);
        var activeStores = _stores.Where(s => wanted.Contains(s.Destination)).ToList();

        var tasks = activeStores.Select(store => Task.Run(async () =>
        {
            try
            {
                var handle = await store.ResolveFolderHandleAsync(projectId, projectFolderId, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(handle))
                    return;

                await foreach (var sf in store.ListFilesAsync(handle, cancellationToken).ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;
                    await channel.Writer.WriteAsync(sf, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected when the project selection changes; swallow.
            }
            catch
            {
                // A single store failing must not abort the whole scan; other stores keep streaming.
            }
        }, cancellationToken)).ToArray();

        _ = Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        await foreach (var sf in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return sf;
    }
}
