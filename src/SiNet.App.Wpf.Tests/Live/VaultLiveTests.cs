using SiNet.Application.Configuration;
using SiNet.Infrastructure.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

[Trait("Category", LiveFactAttribute.Category)]
public sealed class VaultLiveTests
{
    [LiveFact]
    public void WhenLiveEnabledThenAccApiKeyDiagnosticsExposeHashNotRawSecret()
    {
        var vault = LiveEnvironment.CreateVault();
        if (!vault.HasSecret(SecretCatalog.AccServiceApiKey))
        {
            Assert.Fail($"Vault missing {SecretCatalog.AccServiceApiKey}. Provision AccService API key first.");
        }

        var raw = vault.GetSecret(SecretCatalog.AccServiceApiKey);
        Assert.False(string.IsNullOrWhiteSpace(raw));

        var diagnostics = new VaultAccServiceKeyDiagnostics(vault);
        var info = diagnostics.Describe();

        Assert.True(info.HasApiKey);
        Assert.True(info.KeyLength > 0);
        Assert.False(string.IsNullOrWhiteSpace(info.KeyHashPrefix));
        Assert.DoesNotContain(raw!, info.KeyHashPrefix!, StringComparison.Ordinal);
        Assert.Equal(12, info.KeyHashPrefix!.Length);
    }
}
