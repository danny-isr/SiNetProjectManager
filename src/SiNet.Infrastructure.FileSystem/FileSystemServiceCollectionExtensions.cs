using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.FileSystem;

namespace SiNet.Infrastructure.FileSystem;

/// <summary>
/// Modular DI registration for the file-system module.
/// </summary>
public static class FileSystemServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetFileSystem(this IServiceCollection services)
    {
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        return services;
    }
}
