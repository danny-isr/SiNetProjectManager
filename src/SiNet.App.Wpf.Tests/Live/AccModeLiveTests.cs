using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

[Trait("Category", LiveFactAttribute.Category)]
public sealed class AccModeLiveTests
{
    [LiveFact]
    public void WhenLiveEnabledWithBaseUrlThenModeIsRemote()
    {
        var host = new LiveHostConfiguration(LiveEnvironment.AccBaseUrl);
        var services = new ServiceCollection();
        services.AddSingleton<ISecretSetupHostConfiguration>(host);
        services.AddSingleton(LiveEnvironment.CreateVault());
        services.AddSiNetAutodesk();

        using var sp = services.BuildServiceProvider();
        var mode = sp.GetRequiredService<IAccServiceModeProvider>();

        Assert.Equal(AccServiceMode.Remote, mode.Mode);
        Assert.False(string.IsNullOrWhiteSpace(mode.BaseUrl));
    }

    private sealed class LiveHostConfiguration(string baseUrl) : ISecretSetupHostConfiguration
    {
        public string? ActiveDirectoryDomainName => null;
        public string? AccServiceBaseUrl { get; } = baseUrl;
        public IReadOnlyList<string> AccServicePinnedCertificateThumbprints { get; } = [];
    }
}
