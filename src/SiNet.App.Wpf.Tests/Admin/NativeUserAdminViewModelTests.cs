using Microsoft.EntityFrameworkCore;
using Moq;
using SiNet.App.Wpf.Admin.Users;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;
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
            new(1, "Alice", "a@x.com", "alice", false, true, AppAccUserType.NoAccUser, AppRole.Administrator, 2),
        };

        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(users);

        var vm = new UserManagementViewModel(service.Object);
        await vm.LoadUsersAsync();

        Assert.Single(vm.Users);
        Assert.Equal("Alice", vm.Users[0].DisplayName);
        service.Verify(s => s.GetUsersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddUserViewModel_checks_duplicate_before_AddUserAsync()
    {
        var service = new Mock<IUserManagementService>();
        service.Setup(s => s.CheckDuplicateLoginNameAsync("dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var vm = new AddUserViewModel(service.Object)
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

        var vm = new AddUserViewModel(service.Object)
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
        var sut = new SqlUserManagementService(dbFactory.Object, auth.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            sut.AddUserAsync(new CreateUserCommand("user1", "User One"), CancellationToken.None));
    }
}
