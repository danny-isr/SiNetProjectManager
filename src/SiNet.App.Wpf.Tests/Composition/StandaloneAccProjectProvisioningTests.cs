using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Composition;
using SiNet.Application.Projects;
using SiNetSQL.Services.AccBootstrap;
using Xunit;

namespace SiNet.App.Wpf.Tests.Composition;

/// <summary>
/// Guards docs/STANDALONE_NEW_SYSTEM_HOST.md slice 2c: StandaloneNew registers project ACC
/// mapping provisioning; V2Hybrid does not (V2 wires its own block in App.xaml.cs).
/// </summary>
public sealed class StandaloneAccProjectProvisioningTests
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
    public void WhenAddSiNetStandaloneNewThenProjectAccMappingProvisionerIsRegistered()
    {
        var services = new ServiceCollection();

        services.AddSiNet(SiNetHostMode.StandaloneNew, static _ => { });

        Assert.Contains(services, d => d.ServiceType == typeof(IProjectAccMappingProvisioner));
        Assert.Contains(services, d => d.ServiceType == typeof(IAccProjectProvisioningService));
    }

    [Fact]
    public void WhenAddSiNetV2HybridThenCompositionDoesNotRegisterProjectAccProvisioning()
    {
        var services = new ServiceCollection();

        services.AddSiNet(SiNetHostMode.V2Hybrid, static _ => { });

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IProjectAccMappingProvisioner));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAccProjectProvisioningService));
    }

    [Fact]
    public void Composition_registers_project_provisioning_only_for_StandaloneNew()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "SiNet.App.Composition",
            "SiNetCompositionExtensions.cs"));

        Assert.Contains("AddSiNetAccProjectProvisioning", source, StringComparison.Ordinal);
        Assert.Contains("SiNetHostMode.StandaloneNew", source, StringComparison.Ordinal);
    }
}
