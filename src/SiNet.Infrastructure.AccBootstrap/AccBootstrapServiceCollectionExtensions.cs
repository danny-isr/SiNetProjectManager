using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNetSQL.Services.AccBootstrap;

namespace SiNet.Infrastructure.AccBootstrap;

/// <summary>
/// DI registration for in-process ACC Inbox bootstrap used by Standalone New hosts
/// when AccService mode is Local (empty BaseUrl).
/// </summary>
public static class AccBootstrapServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetAccInboxBootstrapLocal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IAccInboxBootstrapLocalExecutor, AccBootstrapLocalInboxBootstrapExecutor>();
        return services;
    }
}
