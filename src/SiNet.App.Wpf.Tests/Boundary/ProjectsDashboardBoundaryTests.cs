using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards for Projects Overview Dashboard — Application ports only, no schema/write paths.
/// </summary>
public sealed class ProjectsDashboardBoundaryTests
{
    private static readonly string[] ForbiddenIdentifiers =
    [
        "SiNetSQL",
        "IDbContextFactory",
        "SaveChanges",
        "Add-Migration",
        "IWorkflowCommandService",
    ];

    [Fact]
    public void ViewModel_uses_dashboard_query_port_only()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Projects/Dashboard/ProjectsDashboardViewModel.cs");

        Assert.Contains("IProjectDashboardQueryService", source, StringComparison.Ordinal);
        Assert.Contains("IProjectFilterOptionsService", source, StringComparison.Ordinal);
        Assert.Contains("ICurrentProjectContext", source, StringComparison.Ordinal);
        Assert.Contains("IProjectWorkSurfaceHost", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IWorkflowCommandService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShell_gates_projects_dashboard_menu_on_feature_code()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("ריכוז פרויקטים", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.ShellOpenProjectsDashboard", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeProjectsDashboard", source, StringComparison.Ordinal);
        Assert.Contains("ProjectsDashboardWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Di_registers_projects_dashboard_window_and_viewmodel()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/NewSystemWpfServiceCollectionExtensions.cs");
        Assert.Contains("ProjectsDashboardViewModel", source, StringComparison.Ordinal);
        Assert.Contains("ProjectsDashboardWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_di_registers_dashboard_query_service()
    {
        var source = ReadRepoFile("src/SiNet.Infrastructure.Sql/ProjectQueryServiceCollectionExtensions.cs");
        Assert.Contains("IProjectDashboardQueryService", source, StringComparison.Ordinal);
        Assert.Contains("ProjectDashboardQueryService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_sources_forbid_write_and_sql_identifiers()
    {
        foreach (var relativePath in EnumerateDashboardFiles())
        {
            var content = ReadRepoFile(relativePath);
            foreach (var forbidden in ForbiddenIdentifiers)
            {
                Assert.False(
                    content.Contains(forbidden, StringComparison.Ordinal),
                    $"'{forbidden}' found in {relativePath}");
            }
        }
    }

    private static IEnumerable<string> EnumerateDashboardFiles()
    {
        var dir = FindRepoRoot();
        var folder = Path.Combine(dir, "src", "SiNet.App.Wpf", "Projects", "Dashboard");
        foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.GetRelativePath(dir, file).Replace('\\', '/');
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("SiNet.sln not found");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
