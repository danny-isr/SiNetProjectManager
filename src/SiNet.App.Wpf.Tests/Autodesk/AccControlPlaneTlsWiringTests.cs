using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Tests.Boundary;
using SiNet.Infrastructure.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

/// <summary>
/// The ACC module used to receive an empty pin list because no composition root passed
/// <c>AccService:PinnedCertificateThumbprints</c> into <c>AddSiNetAutodesk</c>. These tests lock the
/// configuration binding so the pins reach the control-plane HTTP clients too.
/// </summary>
public sealed class AccControlPlaneTlsWiringTests
{
    [Fact]
    public void WhenConfigurationHasPinsThenAddSiNetAutodeskOptionsCarryThem()
    {
        var services = new ServiceCollection();
        services.AddSingleton(BuildConfiguration(new Dictionary<string, string?>
        {
            ["AccService:PinnedCertificateThumbprints:0"] = "AA:BB:CC",
            ["AccService:PinnedCertificateThumbprints:1"] = "DDEEFF",
        }));
        services.AddSiNetAutodesk();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AccServiceControlPlaneOptions>();

        Assert.Equal(["AA:BB:CC", "DDEEFF"], options.PinnedCertificateThumbprints);
    }

    [Fact]
    public void WhenNoConfigurationIsRegisteredThenPinsStayEmpty()
    {
        var services = new ServiceCollection();
        services.AddSiNetAutodesk();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AccServiceControlPlaneOptions>();

        Assert.Empty(options.PinnedCertificateThumbprints);
    }

    [Fact]
    public void WhenHostOverridesPinsThenTheHostCallbackWins()
    {
        var services = new ServiceCollection();
        services.AddSingleton(BuildConfiguration(new Dictionary<string, string?>
        {
            ["AccService:PinnedCertificateThumbprints:0"] = "FROM-CONFIG",
        }));
        services.AddSiNetAutodesk(options => options.PinnedCertificateThumbprints = ["FROM-HOST"]);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AccServiceControlPlaneOptions>();

        Assert.Equal(["FROM-HOST"], options.PinnedCertificateThumbprints);
    }

    [Fact]
    public void WhenReadingPinsThenWhitespaceEntriesAreDropped()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AccService:PinnedCertificateThumbprints:0"] = "  AA11  ",
            ["AccService:PinnedCertificateThumbprints:1"] = "   ",
        });

        var pins = AccServiceControlPlaneConfiguration.ReadPinnedCertificateThumbprints(configuration);

        Assert.Equal(["AA11"], pins);
    }

    [Fact]
    public void LegacyV2GraphPublishesConfigurationSoTheAccModuleCanReadPins()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoPaths.RepoRoot,
            "SiNetProjectManagerV2",
            "Services",
            "Composition",
            "NewSystemServiceCollectionExtensions.cs"));

        Assert.Contains(
            "TryAddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(AppConfiguration.Configuration)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AccControlPlaneDocDescribesPinBasedTlsInsteadOfHostAllowList()
    {
        var doc = File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, "docs", "ACC_CONTROL_PLANE.md"));

        Assert.DoesNotContain("- IP prefixes: `192.168.`", doc, StringComparison.Ordinal);
        Assert.Contains("AccServiceControlPlaneConfiguration.Bind", doc, StringComparison.Ordinal);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
