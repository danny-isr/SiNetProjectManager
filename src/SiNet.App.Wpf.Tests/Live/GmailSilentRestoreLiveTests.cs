using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Common;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Secrets;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

[Trait("Category", LiveFactAttribute.Category)]
public sealed class GmailSilentRestoreLiveTests
{
    [LiveFact]
    public async Task WhenLiveEnabledThenTryRestoreSessionAsyncDoesNotThrowAndNeverOpensBrowser()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSiNetSecrets();
        services.AddSiNetGoogle(static options =>
        {
            options.ApplicationName = "SiNet.LiveSmoke";
            // Interactive sign-in must never be invoked by this test.
            options.AllowInteractiveSignIn = false;
        });

        await using var sp = services.BuildServiceProvider();
        var auth = sp.GetRequiredService<IConnectorAuthService>();

        // Silent restore only — never opens a browser (AllowInteractiveSignIn=false).
        // false = no stored token; true = session restored. Either outcome is valid for live smoke.
        _ = await auth.TryRestoreSessionAsync();
    }
}
