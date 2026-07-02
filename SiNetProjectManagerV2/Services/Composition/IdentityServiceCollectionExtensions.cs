using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Identity;
using SiNetSQL.Services.Users;

namespace SiNetProjectManagerV2.Services.Composition;

/// <summary>
/// Modular DI for Application identity ports and legacy host adapters (see
/// <c>docs/IDENTITY_AND_PERMISSIONS.md</c> §7.6 / P7).
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers identity read/query ports backed by the legacy authenticated user context and services.
    /// </summary>
    public static IServiceCollection AddSiNetIdentityLegacyAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICurrentUserContext, CurrentUserContextAdapter>();
        services.AddSingleton<ICurrentUserProfileService, LegacyCurrentUserProfileService>();
        services.AddSingleton<IAuthorizationQueryService, LegacyAuthorizationQueryService>();
        services.AddSingleton<IActionPermissionQueryService, LegacyActionPermissionQueryService>();
        services.AddSingleton<IUserManagementService, UserManagementPortAdapter>();

        return services;
    }
}
