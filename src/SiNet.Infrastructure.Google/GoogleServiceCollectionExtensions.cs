using Microsoft.Extensions.DependencyInjection;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Modular DI registration for the Google/Gmail module. Real implementations of
/// <c>IEmailGateway</c>, <c>IEmailLabelService</c> and <c>IEmailSyncService</c> are wired here
/// during the Email/Google migration round (or temporarily via <c>SiNet.LegacyBridge</c>).
/// </summary>
public static class GoogleServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetGoogle(this IServiceCollection services)
    {
        // TODO (Email/Google migration round): register IEmailGateway / IEmailLabelService / IEmailSyncService.
        return services;
    }
}
