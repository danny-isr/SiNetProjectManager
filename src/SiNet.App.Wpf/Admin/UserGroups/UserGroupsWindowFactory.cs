using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.UserGroups;

/// <summary>Creates the native user-groups admin window.</summary>
public interface IUserGroupsWindowFactory
{
    UserGroupsWindow Create();
}

/// <summary>DI-backed factory for <see cref="UserGroupsWindow"/>.</summary>
public sealed class UserGroupsWindowFactory(IServiceProvider services) : IUserGroupsWindowFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    public UserGroupsWindow Create()
        => _services.GetRequiredService<UserGroupsWindow>();
}
