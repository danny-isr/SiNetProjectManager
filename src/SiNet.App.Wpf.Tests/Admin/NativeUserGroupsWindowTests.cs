using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Admin.UserGroups;
using SiNet.Application.Identity;
using SiNet.Application.Workflow;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NativeUserGroupsWindowTests
{
    [Fact]
    public void SettingsView_xaml_no_longer_shows_deferred_user_groups()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/Settings/SettingsView.xaml");
        Assert.DoesNotContain("deferred (legacy UserGroupManagementWindow)", xaml, StringComparison.Ordinal);
        Assert.Contains("פתח ניהול קבוצות", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenUserGroupsCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void UserGroupsAdminServiceCollectionExtensions_registers_window_and_factory()
    {
        var services = new ServiceCollection();
        UserGroupsAdminServiceCollectionExtensions.AddSiNetUserGroupsAdminWpf(services);

        Assert.Contains(services, d => d.ServiceType == typeof(UserGroupsWindow));
        Assert.Contains(services, d => d.ServiceType == typeof(UserGroupsViewModel));
        Assert.Contains(services, d => d.ServiceType == typeof(IUserGroupsWindowFactory));
    }

    [Fact]
    public void UserGroupsWindow_can_be_created_via_di()
    {
        RunOnStaThread(() =>
        {
            var window = BuildServiceProvider().GetRequiredService<UserGroupsWindow>();
            Assert.NotNull(window);
            Assert.IsType<UserGroupsView>(window.Content);
            Assert.Contains("הקצאות משתמשים", window.Title, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ProcessBackbone_registers_assignee_readiness_port()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/ProcessBackboneServiceCollectionExtensions.cs");
        Assert.Contains("IWorkflowAssigneeReadinessQueryService", source, StringComparison.Ordinal);
        Assert.Contains("SqlWorkflowAssigneeReadinessQueryService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UserManagementSql_registers_user_group_ports()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/UserManagementServiceCollectionExtensions.cs");
        Assert.Contains("IUserGroupQueryService", source, StringComparison.Ordinal);
        Assert.Contains("IUserGroupCommandService", source, StringComparison.Ordinal);
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
            throw caught;
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddTransient<IUserGroupQueryService, StubQuery>();
        services.AddTransient<IUserGroupCommandService, StubCommand>();
        services.AddTransient<IUserLookupService, StubLookup>();
        services.AddTransient<UserGroupsViewModel>();
        services.AddTransient<UserGroupsView>();
        services.AddTransient<UserGroupsWindow>();
        return services.BuildServiceProvider();
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            var nested = Path.Combine(dir.FullName, "SiNetProjectManager_GitHub", relativePath);
            if (File.Exists(nested))
                return File.ReadAllText(nested);

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} from {AppContext.BaseDirectory}");
    }

    private sealed class StubQuery : IUserGroupQueryService
    {
        public Task<IReadOnlyList<UserGroupSummaryDto>> GetActiveGroupsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UserGroupSummaryDto>>([]);

        public Task<UserGroupDetailDto?> GetGroupDetailAsync(int groupId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserGroupDetailDto?>(null);

        public Task<IReadOnlyList<WorkflowStageGroupDependencyDto>> GetStagesUsingGroupAsync(
            int groupId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkflowStageGroupDependencyDto>>([]);
    }

    private sealed class StubCommand : IUserGroupCommandService
    {
        public Task<int> CreateGroupAsync(string code, string name, string? description = null, CancellationToken cancellationToken = default)
            => Task.FromResult(1);

        public Task UpdateGroupMetadataAsync(int groupId, string code, string name, string? description, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SoftDeleteGroupAsync(int groupId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddMemberAsync(int groupId, int userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveMemberAsync(int groupId, int userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetDefaultAssigneeAsync(int groupId, int? userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubLookup : IUserLookupService
    {
        public Task<IReadOnlyList<UserLookupDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UserLookupDto>>([]);
    }
}
