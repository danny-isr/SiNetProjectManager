using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Admin.Permissions;
using SiNet.App.Wpf.Admin.Users;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NewShellNativeUserAdminMenuTests
{
    [Fact]
    public void NewShell_shows_user_management_menu_only_when_Users_Manage_authorized()
    {
        var items = BuildMenuItems(usersManage: true, actionPermissionsManage: false);
        Assert.Contains(items, i => i.Title == "ניהול משתמשים" && i.IsAvailable);
        Assert.Contains(items, i => i.Title == "הוספת משתמש" && i.IsAvailable);
    }

    [Fact]
    public void NewShell_hides_user_management_menu_when_Users_Manage_denied()
    {
        var items = BuildMenuItems(usersManage: false, actionPermissionsManage: false);
        Assert.DoesNotContain(items, i => i.Title == "ניהול משתמשים");
        Assert.DoesNotContain(items, i => i.Title == "הוספת משתמש");
    }

    [Fact]
    public void NewShell_shows_action_permissions_menu_only_when_ActionPermissions_Manage_authorized()
    {
        var items = BuildMenuItems(usersManage: false, actionPermissionsManage: true);
        Assert.Contains(items, i => i.Title == "הרשאות פעולה" && i.IsAvailable);
    }

    [Fact]
    public void NewShellFactory_opens_native_user_admin_windows_not_legacy()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.Contains("UserListWindow", source, StringComparison.Ordinal);
        Assert.Contains("AddUserDialogWindow", source, StringComparison.Ordinal);
        Assert.Contains("ActionPermissionsWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IUserManagementWindowFactory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IAddUserWindowFactory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IActionPermissionAdminWindowFactory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetProjectManagerV2.Dialogs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new UserManagementWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new AddUserWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ActionPermissionWindow", source, StringComparison.Ordinal);
    }

    private static IReadOnlyList<NewShellMenuItem> BuildMenuItems(bool usersManage, bool actionPermissionsManage)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationQueryService>(new StubAuthorization(usersManage, actionPermissionsManage));
        services.AddTransient<UserListWindow>(_ => new UserListWindow(new UserManagementViewModel(new NoOpUserManagementService(), UserAdminTestDoubles.EmptyMasterPlanLookup())));
        services.AddTransient<AddUserDialogWindow>(_ => new AddUserDialogWindow(new AddUserViewModel(new NoOpUserManagementService(), UserAdminTestDoubles.EmptyMasterPlanLookup())));
        services.AddTransient<ActionPermissionsWindow>(_ => new ActionPermissionsWindow(new ActionPermissionsViewModel(new NoOpActionPermissionAdminService())));
        var sp = services.BuildServiceProvider();
        var factory = new NewShellFactory(sp);

        var method = typeof(NewShellFactory).GetMethod("BuildMigratedOnlyMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (IReadOnlyList<NewShellMenuItem>)method!.Invoke(factory, null)!;
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private sealed class StubAuthorization(bool usersManage, bool actionPermissionsManage) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default)
            => Task.FromResult((usersManage || actionPermissionsManage) && requiredRole == AppRole.Administrator);

        public Task<bool> CanCurrentUserAccessFeatureAsync(string featureCode, CancellationToken cancellationToken = default)
            => Task.FromResult(
                (usersManage && featureCode == AppFeatureCodes.UsersManage)
                || (actionPermissionsManage && featureCode == AppFeatureCodes.ActionPermissionsManage));
    }

    private sealed class NoOpUserManagementService : IUserManagementService
    {
        public Task<IReadOnlyList<UserSummaryDto>> GetUsersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UserSummaryDto>>([]);

        public Task AddUserAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateUsersAsync(IReadOnlyList<UpdateUserCommand> updates, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> CheckDuplicateLoginNameAsync(string loginName, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlySet<string>> GetExistingLoginNamesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    private sealed class NoOpActionPermissionAdminService : IActionPermissionAdminService
    {
        public Task<IReadOnlyList<ActionPermissionAssigneeDto>> GetAssignableUsersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ActionPermissionAssigneeDto>>([]);

        public Task<IReadOnlyDictionary<string, IReadOnlySet<int>>> GetActivePermissionsByActionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlySet<int>>>(new Dictionary<string, IReadOnlySet<int>>());

        public Task SaveActionPermissionsAsync(string actionCode, IReadOnlySet<int> authorizedUserIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveAllActionPermissionsAsync(IReadOnlyDictionary<string, IReadOnlySet<int>> permissionsByActionCode, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
