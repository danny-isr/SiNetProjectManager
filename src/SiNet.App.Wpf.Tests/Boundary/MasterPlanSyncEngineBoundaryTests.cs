using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class MasterPlanSyncEngineBoundaryTests
{
    [Fact]
    public void SyncEngine_shared_no_longer_uses_SiNetSQL_Services_namespace()
    {
        var root = Path.Combine(FindRepoRoot(), "MasterPlan.SyncEngine");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("namespace SiNetSQL.Services", text, StringComparison.Ordinal);
            Assert.DoesNotContain("using SiNetSQL.Services", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SyncEngine_shared_uses_MasterPlan_SyncEngine_Shared_namespace()
    {
        var provider = ReadRepoFile("MasterPlan.SyncEngine/Shared/CredentialProvider.cs");
        Assert.Contains("namespace MasterPlan.SyncEngine.Shared", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncEngine_references_Infrastructure_Logging_and_has_no_Shared_Logging_copy()
    {
        var csproj = ReadRepoFile("MasterPlan.SyncEngine/MasterPlan.SyncEngine.csproj");
        Assert.Contains(
            "SiNet.Infrastructure.Logging\\SiNet.Infrastructure.Logging.csproj",
            csproj,
            StringComparison.Ordinal);

        var program = ReadRepoFile("MasterPlan.SyncEngine/Program.cs");
        Assert.Contains("using SiNet.Infrastructure.Logging;", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MasterPlan.SyncEngine.Shared.Logging", program, StringComparison.Ordinal);

        var sharedLogging = Path.Combine(
            FindRepoRoot(),
            "MasterPlan.SyncEngine",
            "Shared",
            "Logging",
            "CentralLogging.cs");
        Assert.False(File.Exists(sharedLogging), "Shared/Logging/CentralLogging.cs must be removed after S4b cut-over.");
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
