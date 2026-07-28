using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class MasterPlanMappingBoundaryTests
{
    [Fact]
    public void Master_plan_migration_doc_exists_and_approves_s2()
    {
        var doc = ReadRepoFile("docs/MASTER_PLAN_MIGRATION.md");
        Assert.Contains("S2", doc, StringComparison.Ordinal);
        Assert.Contains("IMasterPlanMappingService", doc, StringComparison.Ordinal);
        Assert.DoesNotContain("wrap SiNetSQL MasterPlanMappingViewModel", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Native_mapping_registered_in_sql_and_wpf()
    {
        var sql = ReadRepoFile("src/SiNet.Infrastructure.Sql/UserManagementServiceCollectionExtensions.cs");
        var wpf = ReadRepoFile("src/SiNet.App.Wpf/NewSystemWpfServiceCollectionExtensions.cs");
        var shell = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("IMasterPlanMappingService", sql, StringComparison.Ordinal);
        Assert.Contains("SqlMasterPlanMappingService", sql, StringComparison.Ordinal);
        Assert.Contains("AddSiNetMasterPlanAdminWpf", wpf, StringComparison.Ordinal);
        Assert.Contains("מיפוי MasterPlan", shell, StringComparison.Ordinal);
        Assert.Contains("OpenNativeMasterPlanMapping", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void Mapping_surface_has_no_sinetsql_mvvm_reference()
    {
        var root = Path.Combine(FindRepoRoot(), "src/SiNet.App.Wpf/Admin/MasterPlan");
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            Assert.DoesNotContain("SiNetSQL.MVVM", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CredentialProvider", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LegacyBridge", text, StringComparison.Ordinal);
        }
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
