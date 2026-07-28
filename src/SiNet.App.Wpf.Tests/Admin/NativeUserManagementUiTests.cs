using System.IO;
using Moq;
using SiNet.App.Wpf.Admin.Users;
using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NativeUserManagementUiTests
{
    [Fact]
    public void UserManagementView_row_colors_follow_inactive_admin_active_priority()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/Users/UserManagementView.xaml");

        Assert.Contains("#F1FAF1", xaml, StringComparison.Ordinal);
        Assert.Contains("#F1F6FF", xaml, StringComparison.Ordinal);
        Assert.Contains("#FFF1F1", xaml, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"DataGridCell\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"Transparent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding IsAdministrator", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding IsActive", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void UserManagementView_dirty_column_is_labeled_unsaved()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/Users/UserManagementView.xaml");

        Assert.Contains("Header=\"לא נשמר\"", xaml, StringComparison.Ordinal);
        Assert.Contains("שינוי מקומי שלא נשמר", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"שונה\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void UserEditRow_IsAdministrator_reflects_role()
    {
        var row = new UserEditRow(new UserSummaryDto(
            1, "Admin", "a@x.com", "DOMAIN\\admin", false, true, AppAccUserType.NoAccUser, AppRole.Employee, 0));

        Assert.False(row.IsAdministrator);

        row.Role = AppRole.Administrator;
        Assert.True(row.IsAdministrator);
    }

    [Fact]
    public void UserEditRow_inactive_admin_is_still_administrator_for_row_style_binding()
    {
        var row = new UserEditRow(new UserSummaryDto(
            1, "Admin", "a@x.com", "DOMAIN\\admin", false, false, AppAccUserType.NoAccUser, AppRole.Administrator, 0));

        Assert.True(row.IsAdministrator);
        Assert.False(row.IsActive);
    }

    [Fact]
    public void AddUserView_includes_active_directory_search_section()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/Users/AddUserView.xaml");

        Assert.Contains("חיפוש משתמש ב-Active Directory", xaml, StringComparison.Ordinal);
        Assert.Contains("DirectorySearchResults", xaml, StringComparison.Ordinal);
        Assert.Contains("SearchDirectoryCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AddUserViewModel_directory_selection_fills_login_display_email()
    {
        var service = new Mock<IUserManagementService>();
        var directoryUser = new DirectoryUserDto(@"SI\jdoe", "John Doe", "john@si-eng.local");

        var vm = new AddUserViewModel(
            service.Object,
            UserAdminTestDoubles.EmptyMasterPlanLookup(),
            UserAdminTestDoubles.DirectoryLookupWith(directoryUser));

        vm.SelectedDirectoryUser = directoryUser;

        Assert.Equal(@"SI\jdoe", vm.LoginName);
        Assert.Equal("John Doe", vm.DisplayName);
        Assert.Equal("john@si-eng.local", vm.Email);
        service.Verify(s => s.AddUserAsync(It.IsAny<CreateUserCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddUserViewModel_search_directory_does_not_call_AddUserAsync()
    {
        var service = new Mock<IUserManagementService>();
        var directoryUser = new DirectoryUserDto(@"SI\bob", "Bob Smith", null);

        var vm = new AddUserViewModel(
            service.Object,
            UserAdminTestDoubles.EmptyMasterPlanLookup(),
            UserAdminTestDoubles.DirectoryLookupWith(directoryUser))
        {
            DirectorySearchText = "Bob",
        };

        vm.SearchDirectoryCommand.Execute(null);
        await Task.Delay(200);

        Assert.Single(vm.DirectorySearchResults);
        service.Verify(s => s.AddUserAsync(It.IsAny<CreateUserCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Native_add_user_viewmodel_does_not_reference_legacy_mvvm()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Admin/Users/AddUserViewModel.cs");

        Assert.DoesNotContain("SiNetSQL.MVVM", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveDirectoryService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveDirectoryUserLookupService_does_not_reference_legacy_mvvm()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Secrets/ActiveDirectoryUserLookupService.cs");

        Assert.DoesNotContain("SiNetSQL.MVVM", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
