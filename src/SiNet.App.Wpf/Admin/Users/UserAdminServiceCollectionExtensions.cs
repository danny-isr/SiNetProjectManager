using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.Users;

/// <summary>
/// DI registration for native New System user-admin surfaces (see <c>docs/NEW_SYSTEM_BOUNDARY.md</c>).
/// </summary>
public static class UserAdminServiceCollectionExtensions
{
    /// <summary>
    /// Registers native user-management views, view models, and host windows.
    /// Requires <see cref="SiNet.Application.Identity.IUserManagementService"/> from Infrastructure.Sql.
    /// </summary>
    public static IServiceCollection AddSiNetUserAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IUserAdminChangesNotifier, UserAdminChangesNotifier>();
        services.AddTransient<UserManagementViewModel>();
        services.AddTransient<AddUserViewModel>();
        services.AddTransient<UserManagementView>();
        services.AddTransient<AddUserView>();
        services.AddTransient<UserListWindow>();
        services.AddTransient<AddUserDialogWindow>();

        return services;
    }
}
