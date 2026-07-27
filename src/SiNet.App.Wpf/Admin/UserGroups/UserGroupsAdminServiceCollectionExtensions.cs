using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.UserGroups;

/// <summary>DI registration for native user-group assignment admin surfaces.</summary>
public static class UserGroupsAdminServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetUserGroupsAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<UserGroupsViewModel>();
        services.AddTransient<UserGroupsView>();
        services.AddTransient<UserGroupsWindow>();
        services.AddSingleton<IUserGroupsWindowFactory, UserGroupsWindowFactory>();

        return services;
    }
}
