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
    }

    [Fact]
    public void App_Wpf_has_no_GoogleConnector_ProjectReference()
    {
        var csproj = ReadRepoFile("src/SiNet.App.Wpf/SiNet.App.Wpf.csproj");
        Assert.DoesNotContain("GoogleConnector", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SiNetSQL", csproj, StringComparison.OrdinalIgnoreCase);
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
