using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Guards for docs/ACC_SERVICE_DECOUPLING.md slice B1 (vault + central logging).</summary>
public sealed class AccServiceDecouplingB1BoundaryTests
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

    private static string AccServiceDir => Path.Combine(RepoRoot, "SiOffice.AccService");

    [Fact]
    public void AccService_does_not_use_SiNetSQL_vault_or_central_logging()
    {
        foreach (var file in Directory.EnumerateFiles(AccServiceDir, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("CredentialVaultService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SecretKeys.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SiNetSQL.Services.Logging", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AccService_wires_clean_vault_directly()
    {
        // Superseded by B4: the temporary CredentialProvider.GetSecret bridge was closed —
        // AccService now reads CredentialVault/SecretCatalog directly (see B4 boundary tests).
        var program = File.ReadAllText(Path.Combine(AccServiceDir, "Program.cs"));
        Assert.Contains("CredentialVault.GetSecret", program, StringComparison.Ordinal);
        Assert.Contains("SiNet.Infrastructure.Logging", program, StringComparison.Ordinal);
        Assert.Contains("SecretCatalog.", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CredentialProvider.GetSecret = ", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AccService_csproj_references_Secrets_and_Logging()
    {
        var csproj = File.ReadAllText(Path.Combine(AccServiceDir, "SiOffice.AccService.csproj"));
        Assert.Contains("SiNet.Infrastructure.Secrets", csproj, StringComparison.Ordinal);
        Assert.Contains("SiNet.Infrastructure.Logging", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void CentralLogging_lives_in_Infrastructure_Logging()
    {
        Assert.True(File.Exists(Path.Combine(
            RepoRoot, "src", "SiNet.Infrastructure.Logging", "CentralLogging.cs")));
        Assert.Contains(
            "namespace SiNet.Infrastructure.Logging",
            File.ReadAllText(Path.Combine(
                RepoRoot, "src", "SiNet.Infrastructure.Logging", "CentralLogging.cs")),
            StringComparison.Ordinal);
    }
}
