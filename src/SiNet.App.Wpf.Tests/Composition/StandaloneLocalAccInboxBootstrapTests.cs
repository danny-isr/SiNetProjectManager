using System.IO;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Composition;
using SiNet.Application.Abstractions.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Composition;

/// <summary>
/// Guards docs/STANDALONE_NEW_SYSTEM_HOST.md slice 2b: StandaloneNew registers a local
/// ACC inbox bootstrap executor; V2Hybrid does not (V2 wires LegacyHost separately).
/// </summary>
public sealed class StandaloneLocalAccInboxBootstrapTests
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
    public void WhenAddSiNetStandaloneNewThenLocalInboxBootstrapExecutorIsRegistered()
    {
        var services = new ServiceCollection();

        services.AddSiNet(SiNetHostMode.StandaloneNew, static _ => { });

        Assert.Contains(services, d => d.ServiceType == typeof(IAccInboxBootstrapLocalExecutor));
        Assert.Equal(
            "AccBootstrapLocalInboxBootstrapExecutor",
            services.Single(d => d.ServiceType == typeof(IAccInboxBootstrapLocalExecutor))
                .ImplementationType?.Name);
    }

    [Fact]
    public void WhenAddSiNetV2HybridThenCompositionDoesNotRegisterLocalInboxBootstrapExecutor()
    {
        var services = new ServiceCollection();

        services.AddSiNet(SiNetHostMode.V2Hybrid, static _ => { });

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAccInboxBootstrapLocalExecutor));
    }

    [Fact]
    public void Composition_csproj_references_AccBootstrap()
    {
        var csprojPath = Path.Combine(
            RepoRoot,
            "src",
            "SiNet.App.Composition",
            "SiNet.App.Composition.csproj");

        var references = XDocument.Load(csprojPath)
            .Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(
            references,
            r => r.Contains("SiNet.Infrastructure.AccBootstrap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Composition_registers_local_bootstrap_only_for_StandaloneNew()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "SiNet.App.Composition",
            "SiNetCompositionExtensions.cs"));

        Assert.Contains("AddSiNetAccInboxBootstrapLocal", source, StringComparison.Ordinal);
        Assert.Contains("SiNetHostMode.StandaloneNew", source, StringComparison.Ordinal);
    }
}
