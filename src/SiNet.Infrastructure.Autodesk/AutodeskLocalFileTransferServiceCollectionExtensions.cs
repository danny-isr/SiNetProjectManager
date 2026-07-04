using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Registers only the in-process ACC file-transfer services. Intended for hosts such as
/// <c>SiOffice.AccService</c> that must execute privileged ACC transfer work locally instead of
/// routing back through the remote ACC service boundary.
/// </summary>
public static class AutodeskLocalFileTransferServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetAutodeskLocalFileTransfer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IAccTransferConnector, Bim360AccTransferConnector>();
        services.AddTransient<LocalAccFileUploadService>();
        services.AddTransient<LocalAccFileDownloadService>();
        services.AddTransient<IAccFileUploadService>(sp => sp.GetRequiredService<LocalAccFileUploadService>());
        services.AddTransient<IAccFileDownloadService>(sp => sp.GetRequiredService<LocalAccFileDownloadService>());
        return services;
    }
}
