using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Admin.Users;
using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

/// <summary>
/// Guards native user-admin XAML and DI wiring (IsDirty read-only binding, window resolution).
/// </summary>
public sealed class NativeUserAdminWindowTests
{
    [Fact]
    public void UserManagementView_xaml_IsDirty_binding_is_OneWay_and_read_only()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/Users/UserManagementView.xaml");

        Assert.Contains("IsDirty", xaml, StringComparison.Ordinal);
        Assert.Contains("Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding=\"{Binding IsDirty}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DataGridTemplateColumn Header=\"לא נשמר\"", xaml, StringComparison.Ordinal);
        Assert.Contains("שינוי מקומי שלא נשמר", xaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void UserManagementView_xaml_datagrid_row_uses_star_height_with_min_height_zero()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/Users/UserManagementView.xaml");

        Assert.Contains("Height=\"*\" MinHeight=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("EnableRowVirtualization=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void UserAdminServiceCollectionExtensions_registers_native_user_admin_windows()
    {
        var services = new ServiceCollection();
        UserAdminServiceCollectionExtensions.AddSiNetUserAdminWpf(services);

        Assert.Contains(services, d => d.ServiceType == typeof(UserListWindow));
        Assert.Contains(services, d => d.ServiceType == typeof(AddUserDialogWindow));
        Assert.Contains(services, d => d.ServiceType == typeof(AddUserViewModel));
        Assert.Contains(services, d => d.ServiceType == typeof(UserManagementViewModel));
        Assert.Contains(services, d => d.ServiceType == typeof(AddUserView));
        Assert.Contains(services, d => d.ServiceType == typeof(UserManagementView));
    }

    [Fact]
    public void UserListWindow_can_be_created_via_di()
    {
        RunOnStaThread(() =>
        {
            var window = BuildServiceProvider().GetRequiredService<UserListWindow>();
            Assert.NotNull(window);
            Assert.IsType<UserManagementView>(window.Content);
        });
    }

    [Fact]
    public void AddUserDialogWindow_can_be_created_via_di()
    {
        RunOnStaThread(() =>
        {
            var window = BuildServiceProvider().GetRequiredService<AddUserDialogWindow>();
            Assert.NotNull(window);
            Assert.IsType<AddUserView>(window.Content);
        });
    }

    [Fact]
    public void NewShell_add_user_window_resolves_without_exception_when_services_registered()
    {
        RunOnStaThread(() =>
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAuthorizationQueryService>(new StubAuthorization(usersManage: true));
            RegisterNativeUserAdminWindows(services);

            var window = services.BuildServiceProvider().GetRequiredService<AddUserDialogWindow>();
            Assert.Equal("הוספת משתמש — מערכת חדשה", window.Title);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        if (caught is not null)
        {
            throw caught;
        }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        RegisterNativeUserAdminWindows(services);
        return services.BuildServiceProvider();
    }

    private static void RegisterNativeUserAdminWindows(IServiceCollection services)
    {
        services.AddSingleton<IUserAdminChangesNotifier, UserAdminChangesNotifier>();
        services.AddTransient<UserManagementViewModel>(_ => new UserManagementViewModel(new NoOpUserManagementService(), UserAdminTestDoubles.EmptyMasterPlanLookup()));
        services.AddTransient<AddUserViewModel>(_ => new AddUserViewModel(new NoOpUserManagementService(), UserAdminTestDoubles.EmptyMasterPlanLookup(), UserAdminTestDoubles.EmptyDirectoryLookup()));
        services.AddTransient<UserManagementView>();
        services.AddTransient<AddUserView>();
        services.AddTransient<UserListWindow>();
        services.AddTransient<AddUserDialogWindow>();
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

    private sealed class StubAuthorization(bool usersManage) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default)
            => Task.FromResult(usersManage && requiredRole == AppRole.Administrator);

        public Task<bool> CanCurrentUserAccessFeatureAsync(string featureCode, CancellationToken cancellationToken = default)
            => Task.FromResult(usersManage && featureCode == AppFeatureCodes.UsersManage);
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
}
