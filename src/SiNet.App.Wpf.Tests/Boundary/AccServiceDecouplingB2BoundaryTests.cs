using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Guards for docs/ACC_SERVICE_DECOUPLING.md slice B2 (contracts extraction).</summary>
public sealed class AccServiceDecouplingB2BoundaryTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate repo root (SiNet.sln).");
        }
    }

    [Fact]
    public void Contracts_project_exists_with_canonical_namespace()
    {
        var path = Path.Combine(
            RepoRoot, "src", "SiOffice.AccService.Contracts", "AccServiceContracts.cs");
        Assert.True(File.Exists(path));
        var source = File.ReadAllText(path);
        Assert.Contains("namespace SiOffice.AccService.Contracts", source, StringComparison.Ordinal);
        Assert.Contains("ApiVersionPrefix", source, StringComparison.Ordinal);
        Assert.Contains("EnsureInboxResponse", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AccServiceContractConstants_mirror_is_deleted()
    {
        Assert.False(File.Exists(Path.Combine(
            RepoRoot, "src", "SiNet.Infrastructure.Autodesk", "AccServiceContractConstants.cs")));
    }

    [Fact]
    public void AccService_and_Autodesk_do_not_use_SiNetSQL_contracts_namespace()
    {
        var roots = new[]
        {
            Path.Combine(RepoRoot, "SiOffice.AccService"),
            Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Autodesk"),
        };

        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(file);
                Assert.DoesNotContain(
                    "SiNetSQL.Services.AccBootstrap.Contracts",
                    source,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Autodesk_csproj_references_AccService_Contracts()
    {
        var csproj = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiNet.Infrastructure.Autodesk", "SiNet.Infrastructure.Autodesk.csproj"));
        Assert.Contains("SiOffice.AccService.Contracts", csproj, StringComparison.Ordinal);
    }
}
