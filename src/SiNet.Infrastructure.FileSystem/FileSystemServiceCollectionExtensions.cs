using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.Abstractions.FileSystem;
using SiNet.Application.ProjectWork;
using SiNet.Infrastructure.FileSystem.ProjectWork;

namespace SiNet.Infrastructure.FileSystem;

/// <summary>
/// Modular DI registration for the file-system module.
/// </summary>
public static class FileSystemServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetFileSystem(this IServiceCollection services)
    {
        services.TryAddSingleton<IFileStorage, LocalFileStorage>();

        // ProjectWork FileServer file store (read + local staging). Registered as one of the
        // IFileStore backends consumed by the FileIndex coordinator.
        if (!services.Any(d =>
                d.ServiceType == typeof(IFileStore)
                && d.ImplementationType == typeof(FileServerFileStore)))
        {
            services.AddSingleton<IFileStore, FileServerFileStore>();
        }

        // Per-surface debounced file-server watcher for live tree rescans.
        services.TryAddTransient<IFileServerWatcher, FileServerWatcher>();

        return services;
    }
}
