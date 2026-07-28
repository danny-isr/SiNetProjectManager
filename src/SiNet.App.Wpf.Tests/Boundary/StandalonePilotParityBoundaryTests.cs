using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Guards for docs/STANDALONE_NEW_SYSTEM_HOST.md slice 2 (pilot parity).</summary>
public sealed class StandalonePilotParityBoundaryTests
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
    public void Standalone_host_registers_vault_token_ad_masterplan_and_catalog()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiNet.App.Wpf", "StandaloneHostServiceCollectionExtensions.cs"));

        Assert.Contains("AddSiNetAutodeskVaultTokenProvider", source, StringComparison.Ordinal);
        Assert.Contains("VaultDirectoryUserConnectionProvider", source, StringComparison.Ordinal);
        Assert.Contains("ActiveDirectoryUserLookupService", source, StringComparison.Ordinal);
        Assert.Contains("VaultMasterPlanEmployeeConnectionProvider", source, StringComparison.Ordinal);
        Assert.Contains("GoogleDriveInspectionTemplateCatalog", source, StringComparison.Ordinal);
        Assert.Contains("MutableSecretSetupHostConfiguration", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkSurfaces_register_IProjectWorkSurfaceHost()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiNet.App.Wpf", "WorkSurfaces", "WorkSurfaceServiceCollectionExtensions.cs"));

        Assert.Contains("IProjectWorkSurfaceHost", source, StringComparison.Ordinal);
        Assert.Contains("ProjectWorkSurfaceHost", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveDirectory_lookup_lives_in_Infrastructure_Secrets()
    {
        Assert.True(File.Exists(Path.Combine(
            RepoRoot, "src", "SiNet.Infrastructure.Secrets", "ActiveDirectoryUserLookupService.cs")));
        Assert.False(File.Exists(Path.Combine(
            RepoRoot, "SiNetProjectManagerV2", "Services", "ActiveDirectoryUserLookupService.cs")));
    }

    [Fact]
    public void App_startup_applies_Acc_host_config_from_system_settings()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "SiNet.App.Wpf", "App.xaml.cs"));
        Assert.Contains("ApplyAccHostConfigFromSystemSettingsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplySystemSettings", source, StringComparison.Ordinal);
    }
}
