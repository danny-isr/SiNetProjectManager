using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SiNet.App.Wpf.Admin.Users;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.MasterPlan;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

public sealed class NativeUserManagementParityTests
{
    [Fact]
    public void UserManagementView_uses_masterplan_combobox_not_textbox_for_employee_id()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/Users/UserManagementView.xaml");

        Assert.Contains("MasterPlanEmployees", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"Id\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberPath=\"Name\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{Binding MasterPlanEmployeeId", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding MasterPlanEmployeeId", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void UserManagementView_displays_and_edits_AccUserType()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Admin/Users/UserManagementView.xaml");

        Assert.Contains("ACC הרשאת", xaml, StringComparison.Ordinal);
        Assert.Contains("AccUserTypeDisplay", xaml, StringComparison.Ordinal);
        Assert.Contains("AvailableAccUserTypes", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding AccUserType", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_user_management_window_core_fields_are_covered_by_native_view()
    {
        var native = ReadRepoFile("src/SiNet.App.Wpf/Admin/Users/UserManagementView.xaml");

        foreach (var field in new[]
                 {
                     "שם", "Email", "LoginName", "ACC", "תפקיד", "MasterPlan", "משימות פתוחות",
                     "רענון", "שמור", "ביטול", "חיפוש",
                 })
        {
            Assert.True(
                native.Contains(field, StringComparison.Ordinal),
                $"Expected native user management to cover legacy field marker '{field}'");
        }
    }

    [Fact]
    public async Task UserManagementViewModel_loads_MasterPlanEmployees()
    {
        var employees = new List<MasterPlanEmployeeDto>
        {
            new(null, "-- ללא קישור --"),
            new(10, "Employee Ten", SourceDatabase: "Replica"),
        };

        var lookup = new Mock<IMasterPlanEmployeeLookupService>();
        lookup.Setup(s => s.GetEmployeesAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(employees);

        var users = new Mock<IUserManagementService>();
        users.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserSummaryDto(1, "A", "a@x.com", "a", false, true, AppAccUserType.Engineer, AppRole.Employee, 0, 10)]);

        var vm = new UserManagementViewModel(users.Object, lookup.Object);
        await vm.LoadUsersAsync();

        Assert.Equal(2, vm.MasterPlanEmployees.Count);
        Assert.Equal("Employee Ten", vm.Users[0].MasterPlanEmployeeName);
        lookup.Verify(s => s.GetEmployeesAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UserManagementViewModel_save_includes_AccUserType_and_MasterPlanEmployeeId()
    {
        IReadOnlyList<UpdateUserCommand>? captured = null;
        var users = new Mock<IUserManagementService>();
        users.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new UserSummaryDto(1, "A", "a@x.com", "a", false, true, AppAccUserType.NoAccUser, AppRole.Employee, 0)]);
        users.Setup(s => s.UpdateUsersAsync(It.IsAny<IReadOnlyList<UpdateUserCommand>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<UpdateUserCommand>, CancellationToken>((updates, _) => captured = updates)
            .Returns(Task.CompletedTask);

        var lookup = new Mock<IMasterPlanEmployeeLookupService>();
        lookup.Setup(s => s.GetEmployeesAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new(null, "--"), new(5, "Emp")]);

        var vm = new UserManagementViewModel(users.Object, lookup.Object);
        await vm.LoadUsersAsync();

        vm.Users[0].AccUserType = AppAccUserType.Admin;
        vm.Users[0].MasterPlanEmployeeId = 5;
        vm.NotifyRowChanged();
        await vm.SaveChangesAsync();

        Assert.NotNull(captured);
        Assert.Equal(AppAccUserType.Admin, captured![0].AccUserType);
        Assert.Equal(5, captured[0].MasterPlanEmployeeId);
    }

    [Fact]
    public void SqlMasterPlanEmployeeLookupService_does_not_reference_SiNetSQL_MVVM()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/MasterPlan/SqlMasterPlanEmployeeLookupService.cs");
        Assert.DoesNotContain("SiNetSQL.MVVM", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.Models", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplicaR03Repository", source, StringComparison.Ordinal);
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
}
