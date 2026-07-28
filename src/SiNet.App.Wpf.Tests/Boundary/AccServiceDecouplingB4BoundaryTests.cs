using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards for docs/ACC_SERVICE_DECOUPLING.md slice B4: AccBootstrap/provisioning types were
/// extracted out of SiNetSQL into <c>SiNet.Infrastructure.AccBootstrap</c>, and the
/// CredentialProvider / legacy SystemSettingsService bridges were closed in SiOffice.AccService.
/// </summary>
public sealed class AccServiceDecouplingB4BoundaryTests
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

    private static string AccBootstrapProjectFile =>
        Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.AccBootstrap", "SiNet.Infrastructure.AccBootstrap.csproj");

    [Fact]
    public void AccBootstrap_project_exists()
    {
        Assert.True(
            File.Exists(AccBootstrapProjectFile),
            $"Expected {AccBootstrapProjectFile} to exist (B4 extraction from SiNetSQL).");
    }

    [Fact]
    public void AccService_csproj_references_AccBootstrap_project()
    {
        var csproj = File.ReadAllText(Path.Combine(AccServiceDir, "SiOffice.AccService.csproj"));
        Assert.Contains("SiNet.Infrastructure.AccBootstrap.csproj", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void AccService_csproj_does_not_reference_SiNetSQL()
    {
        // B4 goal achieved: AccService no longer needs the legacy SiNetSQL assembly for anything.
        var csproj = File.ReadAllText(Path.Combine(AccServiceDir, "SiOffice.AccService.csproj"));
        Assert.DoesNotContain("SiNetSQL.csproj", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_does_not_bridge_CredentialProvider()
    {
        var program = File.ReadAllText(Path.Combine(AccServiceDir, "Program.cs"));
        Assert.DoesNotContain("CredentialProvider.GetSecret =", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.Services.CredentialProvider", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_does_not_register_legacy_SystemSettingsService()
    {
        var program = File.ReadAllText(Path.Combine(AccServiceDir, "Program.cs"));
        Assert.DoesNotContain("AddSingleton<SystemSettingsService>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("using SiNetSQL.Services;", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_resolves_TokenProvider_credentials_from_vault_directly()
    {
        var program = File.ReadAllText(Path.Combine(AccServiceDir, "Program.cs"));
        Assert.Contains("CredentialVault.GetSecret(SecretCatalog.AutodeskClientId)", program, StringComparison.Ordinal);
        Assert.Contains("CredentialVault.GetSecret(SecretCatalog.AutodeskClientSecret)", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AccEndpoints_does_not_use_CredentialProvider()
    {
        var endpoints = File.ReadAllText(Path.Combine(AccServiceDir, "Endpoints", "AccEndpoints.cs"));
        Assert.DoesNotContain("CredentialProvider.", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("using SiNetSQL.Services;", endpoints, StringComparison.Ordinal);
    }
}
