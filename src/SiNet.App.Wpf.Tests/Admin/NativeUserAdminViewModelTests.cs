using Microsoft.EntityFrameworkCore;
using Moq;
using SiNet.App.Wpf.Admin.Permissions;
using SiNet.App.Wpf.Admin.Users;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;
using SiNet.Infrastructure.Sql.Entities;
using SiNet.Infrastructure.Sql.Services.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NativeUserAdminViewModelTests
{
    [Fact]
    public async Task UserManagementViewModel_loads_users_via_GetUsersAsync()
    {
        var users = new List<UserSummaryDto>
        {
            new(1, "Alice", "a@x.com", "alice", false, true, AppAccUserType.NoAccUser, AppRole.Administrator, 2, Notes: "note"),
        };

        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(users);

        var vm = new UserManagementViewModel(service.Object, UserAdminTestDoubles.EmptyMasterPlanLookup());
        await vm.LoadUsersAsync();

        Assert.Single(vm.Users);
        Assert.Equal("Alice", vm.Users[0].DisplayName);
        Assert.Equal("note", vm.Users[0].Notes);
        service.Verify(s => s.GetUsersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UserManagementViewModel_save_sends_UpdateUsersAsync_for_dirty_rows()
    {
        IReadOnlyList<UpdateUserCommand>? captured = null;
        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserSummaryDto(1, "Alice", "a@x.com", "alice", false, true, AppAccUserType.NoAccUser, AppRole.Administrator, 0)]);
        service.Setup(s => s.UpdateUsersAsync(It.IsAny<IReadOnlyList<UpdateUserCommand>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<UpdateUserCommand>, CancellationToken>((updates, _) => captured = updates)
            .Returns(Task.CompletedTask);

        var vm = new UserManagementViewModel(service.Object, UserAdminTestDoubles.EmptyMasterPlanLookup());
        await vm.LoadUsersAsync();
        vm.Users[0].DisplayName = "Alice Updated";
        vm.NotifyRowChanged();

        await vm.SaveChangesAsync();

        Assert.NotNull(captured);
        Assert.Single(captured!);
        Assert.Equal("Alice Updated", captured![0].DisplayName);
    }

    [Fact]
    public async Task UserManagementViewModel_cancel_reverts_dirty_rows()
    {
        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserSummaryDto(1, "Alice", "a@x.com", "alice", false, true, AppAccUserType.NoAccUser, AppRole.Administrator, 0)]);

        var vm = new UserManagementViewModel(service.Object, UserAdminTestDoubles.EmptyMasterPlanLookup());
        await vm.LoadUsersAsync();
        vm.Users[0].DisplayName = "Changed";
        vm.NotifyRowChanged();
        vm.CancelCommand.Execute(null);

        Assert.Equal("Alice", vm.Users[0].DisplayName);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task AddUserViewModel_checks_duplicate_before_AddUserAsync()
    {
        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.CheckDuplicateLoginNameAsync("dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = new AddUserViewModel(service.Object, UserAdminTestDoubles.EmptyMasterPlanLookup(), UserAdminTestDoubles.EmptyDirectoryLookup())
        {
            LoginName = "dup",
            DisplayName = "Dup User",
        };

        await vm.SaveAsync();

        Assert.Contains("כבר קיים", vm.ValidationMessage, StringComparison.Ordinal);
        service.Verify(s => s.AddUserAsync(It.IsAny<CreateUserCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddUserViewModel_sends_CreateUserCommand_with_Notes()
    {
        CreateUserCommand? captured = null;
        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.CheckDuplicateLoginNameAsync("new1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        service.Setup(s => s.AddUserAsync(It.IsAny<CreateUserCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateUserCommand, CancellationToken>((cmd, _) => captured = cmd)
            .Returns(Task.CompletedTask);

        var vm = new AddUserViewModel(service.Object, UserAdminTestDoubles.EmptyMasterPlanLookup(), UserAdminTestDoubles.EmptyDirectoryLookup())
        {
            LoginName = "new1",
            DisplayName = "New User",
            Notes = "onboarding note",
        };

        await vm.SaveAsync();

        Assert.NotNull(captured);
        Assert.Equal("onboarding note", captured!.Notes);
        service.Verify(s => s.AddUserAsync(It.IsAny<CreateUserCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddUserViewModel_notifies_changes_after_successful_add()
    {
        var notifier = new UserAdminChangesNotifier();
        var notified = false;
        notifier.UsersChanged += (_, _) => notified = true;

        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.CheckDuplicateLoginNameAsync("new1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        service.Setup(s => s.AddUserAsync(It.IsAny<CreateUserCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var vm = new AddUserViewModel(service.Object, UserAdminTestDoubles.EmptyMasterPlanLookup(), UserAdminTestDoubles.EmptyDirectoryLookup(), notifier)
        {
            LoginName = "new1",
            DisplayName = "New User",
        };

        await vm.SaveAsync();
        Assert.True(notified);
    }

    [Fact]
    public async Task UserManagementViewModel_manual_refresh_skips_when_unsaved_changes()
    {
        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserSummaryDto(1, "Alice", "a@x.com", "alice", false, true, AppAccUserType.NoAccUser, AppRole.Administrator, 0)]);

        var vm = new UserManagementViewModel(service.Object, UserAdminTestDoubles.EmptyMasterPlanLookup());
        await vm.LoadUsersAsync();
        vm.Users[0].DisplayName = "Changed";
        vm.NotifyRowChanged();

        await vm.LoadUsersAsync(force: false);

        Assert.True(vm.HasUnsavedChanges);
        Assert.Equal("Changed", vm.Users[0].DisplayName);
        Assert.Contains("שינויים שלא נשמרו", vm.StatusMessage, StringComparison.Ordinal);
        service.Verify(s => s.GetUsersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UserManagementViewModel_force_reload_includes_new_user_despite_unsaved_changes()
    {
        var call = 0;
        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;
                if (call == 1)
                {
                    return
                    [
                        new UserSummaryDto(1, "Alice", "a@x.com", "alice", false, true, AppAccUserType.NoAccUser, AppRole.Administrator, 0),
                    ];
                }

                return
                [
                    new UserSummaryDto(1, "Alice", "a@x.com", "alice", false, true, AppAccUserType.NoAccUser, AppRole.Administrator, 0),
                    new UserSummaryDto(2, "Bob", "b@x.com", "bob", false, true, AppAccUserType.NoAccUser, AppRole.Employee, 0),
                ];
            });

        var notifier = new UserAdminChangesNotifier();
        var vm = new UserManagementViewModel(service.Object, UserAdminTestDoubles.EmptyMasterPlanLookup(), notifier);
        await vm.LoadUsersAsync();
        vm.Users[0].DisplayName = "Changed";
        vm.NotifyRowChanged();
        Assert.True(vm.HasUnsavedChanges);

        await vm.LoadUsersAsync(force: true);

        Assert.Equal(2, vm.Users.Count);
        Assert.Contains(vm.Users, u => u.DisplayName == "Bob");
        Assert.False(vm.HasUnsavedChanges);
        Assert.Equal("Alice", vm.Users[0].DisplayName);
    }

    [Fact]
    public async Task UserManagementViewModel_UsersChanged_triggers_force_reload()
    {
        var call = 0;
        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;
                if (call == 1)
                {
                    return
                    [
                        new UserSummaryDto(1, "Alice", "a@x.com", "alice", false, true, AppAccUserType.NoAccUser, AppRole.Administrator, 0),
                    ];
                }

                return
                [
                    new UserSummaryDto(1, "Alice", "a@x.com", "alice", false, true, AppAccUserType.NoAccUser, AppRole.Administrator, 0),
                    new UserSummaryDto(2, "Bob", "b@x.com", "bob", false, true, AppAccUserType.NoAccUser, AppRole.Employee, 0),
                ];
            });

        var notifier = new UserAdminChangesNotifier();
        var vm = new UserManagementViewModel(service.Object, UserAdminTestDoubles.EmptyMasterPlanLookup(), notifier);
        await vm.LoadUsersAsync();
        vm.Users[0].DisplayName = "Changed";
        vm.NotifyRowChanged();

        notifier.NotifyUsersChanged();
        await WaitUntilAsync(() => vm.Users.Count == 2 && !vm.IsLoading, TimeSpan.FromSeconds(3));

        Assert.Equal(2, vm.Users.Count);
        Assert.Contains(vm.Users, u => u.DisplayName == "Bob");
        Assert.False(vm.HasUnsavedChanges);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(20);
        }

        Assert.True(condition(), "Timed out waiting for condition.");
    }
}

public sealed class SqlUserManagementServiceAuthorizationTests
{
    [Fact]
    public async Task AddUserAsync_throws_when_caller_lacks_Users_Manage()
    {
        var auth = new Mock<IAuthorizationQueryService>();
        auth.Setup(a => a.CanCurrentUserAccessFeatureAsync(AppFeatureCodes.UsersManage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var dbFactory = new Mock<IDbContextFactory<SiNetDbContext>>();
        var sut = new SqlUserManagementService(dbFactory.Object, auth.Object, NullCurrentUserContext.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.AddUserAsync(new CreateUserCommand("user1", "User One"), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUsersAsync_throws_when_caller_lacks_Users_Manage()
    {
        var auth = new Mock<IAuthorizationQueryService>();
        auth.Setup(a => a.CanCurrentUserAccessFeatureAsync(AppFeatureCodes.UsersManage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var dbFactory = new Mock<IDbContextFactory<SiNetDbContext>>();
        var sut = new SqlUserManagementService(dbFactory.Object, auth.Object, NullCurrentUserContext.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.UpdateUsersAsync([new UpdateUserCommand(1, "A", null, "a", AppAccUserType.NoAccUser, AppRole.Employee, true)], CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUsersAsync_blocks_self_deactivate_when_current_user_bound()
    {
        var db = CreateInMemoryDb();
        var auth = AuthorizeUsersManage();
        var currentUser = new StubCurrentUserContext(1);
        var sut = new SqlUserManagementService(db, auth.Object, currentUser);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdateUsersAsync(
                [new UpdateUserCommand(1, "Admin", "a@x.com", "admin", AppAccUserType.NoAccUser, AppRole.Administrator, false)],
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUsersAsync_blocks_duplicate_login_name()
    {
        var db = CreateInMemoryDb();
        var auth = AuthorizeUsersManage();
        var sut = new SqlUserManagementService(db, auth.Object, NullCurrentUserContext.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdateUsersAsync(
                [new UpdateUserCommand(2, "Bob", null, "admin", AppAccUserType.NoAccUser, AppRole.Employee, true)],
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUsersAsync_persists_MasterPlanEmployeeId_and_AccUserType()
    {
        var db = CreateInMemoryDb();
        var auth = AuthorizeUsersManage();
        var sut = new SqlUserManagementService(db, auth.Object, NullCurrentUserContext.Instance);

        await sut.UpdateUsersAsync(
            [new UpdateUserCommand(2, "Bob", "b@x.com", "bob", AppAccUserType.Engineer, AppRole.Employee, true, MasterPlanEmployeeId: 42)],
            CancellationToken.None);

        await using var verify = db.CreateDbContext();
        var entity = await verify.Users.SingleAsync(u => u.Id == 2);
        Assert.Equal(42, entity.MasterPlanEmployeeId);
        Assert.Equal((int)AppAccUserType.Engineer, entity.AccUserType);
    }

    private static Mock<IAuthorizationQueryService> AuthorizeUsersManage()
    {
        var auth = new Mock<IAuthorizationQueryService>();
        auth.Setup(a => a.CanCurrentUserAccessFeatureAsync(AppFeatureCodes.UsersManage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return auth;
    }

    private static TestDbFactory CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<SiNetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var context = new SiNetDbContext(options))
        {
            context.Users.AddRange(
                new SiUserEntity { Id = 1, Name = "Admin", LoginName = "admin", Email = "a@x.com", Role = (int)AppRole.Administrator, IsActive = true },
                new SiUserEntity { Id = 2, Name = "Alice", LoginName = "alice", Email = "alice@x.com", Role = (int)AppRole.Employee, IsActive = true });
            context.SaveChanges();
        }

        return new TestDbFactory(options);
    }

    private sealed class StubCurrentUserContext(int userId) : ICurrentUserContext
    {
        public int? UserId => userId;
    }

    private sealed class TestDbFactory(DbContextOptions<SiNetDbContext> options)
        : IDbContextFactory<SiNetDbContext>
    {
        public SiNetDbContext CreateDbContext() => new(options);

        public ValueTask<SiNetDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(new SiNetDbContext(options));
    }
}

public sealed class SqlActionPermissionAdminServiceAuthorizationTests
{
    [Fact]
    public async Task SaveActionPermissionsAsync_throws_when_caller_lacks_ActionPermissions_Manage()
    {
        var auth = new Mock<IAuthorizationQueryService>();
        auth.Setup(a => a.CanCurrentUserAccessFeatureAsync(AppFeatureCodes.ActionPermissionsManage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var dbFactory = new Mock<IDbContextFactory<SiNetDbContext>>();
        var sut = new SqlActionPermissionAdminService(dbFactory.Object, auth.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.SaveActionPermissionsAsync(ActionPermissionCodes.NewProjectDialog, new HashSet<int>(), CancellationToken.None));
    }
}

public sealed class ActionPermissionsViewModelTests
{
    [Fact]
    public async Task ActionPermissionsViewModel_loads_actions_from_catalog()
    {
        var admin = new Mock<IActionPermissionAdminService>();
        admin.Setup(s => s.GetAssignableUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActionPermissionAssigneeDto>());
        admin.Setup(s => s.GetActivePermissionsByActionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlySet<int>>());

        var vm = new ActionPermissionsViewModel(admin.Object);
        await vm.LoadAsync();

        Assert.Equal(ActionPermissionCatalog.All.Count, vm.Actions.Count);
    }
}
