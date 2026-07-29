using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.MasterPlan;

public sealed class MasterPlanReportsBoundaryTests
{
    [Fact]
    public void GmailClientProvider_scopes_include_spreadsheets()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailClientProvider.cs");
        Assert.Contains("SheetsService.Scope.Spreadsheets", source, StringComparison.Ordinal);
        Assert.Contains("TryGetSheetsServiceAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AddSiNetGoogle_registers_native_report_services()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Google/GoogleServiceCollectionExtensions.cs");
        Assert.Contains("IMasterPlanR03ReportService", source, StringComparison.Ordinal);
        Assert.Contains("NativeR03ReportService", source, StringComparison.Ordinal);
        Assert.Contains("IMasterPlanR01ReportService", source, StringComparison.Ordinal);
        Assert.Contains("IMasterPlanR02ReportService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AddSiNetUserManagementSql_registers_report_data_sources()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/UserManagementServiceCollectionExtensions.cs");
        Assert.Contains("IR03ReportDataSource", source, StringComparison.Ordinal);
        Assert.Contains("SqlR03ReportDataSource", source, StringComparison.Ordinal);
        Assert.Contains("IR01ReportDataSource", source, StringComparison.Ordinal);
        Assert.Contains("IR02ReportDataSource", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShell_registers_reports_menu_under_ReportsManagement()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.Contains("AppFeatureCodes.ReportsManagement", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeR01Report", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeR02Report", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeR03Report", source, StringComparison.Ordinal);
        // R03 preview is available to every authenticated user; R01/R02 stay management-gated.
        Assert.Contains("HasAuthenticatedUser()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void R03_window_has_employee_checklist_for_management()
    {
        var window = ReadRepoFile("src/SiNet.App.Wpf/Admin/MasterPlan/Reports/R03ReportWindow.xaml");
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Admin/MasterPlan/Reports/R03ReportViewModel.cs");
        Assert.Contains("IsAdminMode", window, StringComparison.Ordinal);
        Assert.Contains("SelectAllEmployeesCommand", window, StringComparison.Ordinal);
        Assert.Contains("ClearEmployeeSelectionCommand", window, StringComparison.Ordinal);
        Assert.Contains("FilteredEmployees", window, StringComparison.Ordinal);
        Assert.Contains("ICurrentUserProfileService", vm, StringComparison.Ordinal);
        Assert.Contains("MasterPlanEmployeeId", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void App_Wpf_has_no_GoogleConnector_ProjectReference()
    {
        var csproj = ReadRepoFile("src/SiNet.App.Wpf/SiNet.App.Wpf.csproj");
        Assert.DoesNotContain("GoogleConnector", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SiNetSQL", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void R03_service_exposes_in_app_preview_without_google()
    {
        var port = ReadRepoFile("src/SiNet.Application/MasterPlan/Reports/IMasterPlanR03ReportService.cs");
        var native = ReadRepoFile("src/SiNet.Infrastructure.Google/Reports/NativeR03ReportService.cs");
        var window = ReadRepoFile("src/SiNet.App.Wpf/Admin/MasterPlan/Reports/R03ReportWindow.xaml");
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Admin/MasterPlan/Reports/R03ReportViewModel.cs");

        Assert.Contains("PreviewAsync", port, StringComparison.Ordinal);
        Assert.Contains("PreviewAsync", native, StringComparison.Ordinal);
        Assert.Contains("PreviewCommand", vm, StringComparison.Ordinal);
        Assert.Contains("הצג נתונים", window, StringComparison.Ordinal);
        Assert.Contains("DataGrid", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_buttons_use_rounded_corner_template()
    {
        var styles = ReadRepoFile("src/SiNet.App.Wpf/Theme/ThemeStyles.xaml");
        Assert.Contains("SiRoundedButtonBase", styles, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"6\"", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void R03PreviewResult_difference_and_fail_helpers()
    {
        var row = new SiNet.Application.MasterPlan.Reports.R03DailyPreviewRow(
            1, "A", new DateTime(2026, 7, 1), "ד'", 8m, 6m);
        Assert.True(row.IsNegativeDifference);
        Assert.Equal(-2m, row.Difference);

        var fail = SiNet.Application.MasterPlan.Reports.R03PreviewResult.Fail("x");
        Assert.False(fail.Success);
        Assert.Equal("x", fail.Error);
    }

    [Fact]
    public void R02_full_internal_headers_include_description_and_subcontract()
    {
        var headers = SiNet.Application.MasterPlan.Reports.R02HoursRow.GetHeaderRow(isClientExport: false);
        Assert.Equal(20, headers.Count);
        Assert.Contains("תיאור", headers);
        Assert.Contains("שם תת-חוזה", headers);
        Assert.Contains("שם שלב", headers);

        var sql = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/MasterPlan/Reports/SqlR02ReportDataSource.cs");
        Assert.Contains("hr.Description", sql, StringComparison.Ordinal);
        Assert.Contains("SubContracts", sql, StringComparison.Ordinal);
        Assert.Contains("MP_ProjectHoursExtended", sql, StringComparison.Ordinal);
        Assert.Contains("ph.SubContractName", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("GROUP BY", sql, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                || File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found.");
    }
}
