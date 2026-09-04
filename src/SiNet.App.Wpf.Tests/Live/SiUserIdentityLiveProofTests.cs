using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Common;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Services.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// Live DEV proof for SIUser identity gate (requires <c>SINET_LIVE_SMOKE=1</c> + SQL connection).
/// </summary>
public sealed class SiUserIdentityLiveProofTests
{
    [LiveFact]
    public async Task Live_resolves_siuser_and_evaluates_google_coherence_without_sending_mail()
    {
        var cs = LiveEnvironment.TryResolveSqlConnectionString();
        Assert.False(string.IsNullOrWhiteSpace(cs));

        var services = new ServiceCollection();
        services.AddSiNetLogging();
        services.AddSiNetSql(cs!);
        services.AddSiNetIdentitySql();
        services.AddSiNetGoogle(static options =>
        {
            options.ApplicationName = "SiNet.IdentityLiveProof";
            options.AllowInteractiveSignIn = false;
            options.TokenStorePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiNet",
                "google-token");
        });

        await using var sp = services.BuildServiceProvider();
        var authenticator = sp.GetRequiredService<SqlWindowsCurrentUserAuthenticator>();
        var auth = await authenticator.AuthenticateAsync();

        Assert.NotEqual(WindowsUserAuthStatus.Blocked, auth.Status);
        Assert.NotNull(auth.Profile);
        Assert.True(auth.Profile!.UserId > 0);

        var google = sp.GetRequiredService<IConnectorAuthService>();
        _ = await google.TryRestoreSessionAsync();

        var coherence = sp.GetRequiredService<IIdentityCoherenceService>();
        // Do not disconnect Google during live proof — only observe.
        var snap = await coherence.EvaluateAsync(
            new IdentityCoherenceEvaluateOptions(DisconnectGoogleOnMismatch: false));

        Assert.Equal(auth.Profile.UserId, snap.SiUserId);
        Assert.Equal(auth.Profile.Email, snap.SiUserEmail);
        Assert.Equal(AccAuthMode.ApplicationTwoLegged, snap.AccAuthMode);

        // Controlled mismatch without sending mail: evaluate with a fake three-legged email.
        if (!string.IsNullOrWhiteSpace(snap.SiUserEmail))
        {
            var mismatch = await coherence.EvaluateAsync(new IdentityCoherenceEvaluateOptions(
                DisconnectGoogleOnMismatch: false,
                AutodeskThreeLeggedEmail: "mismatch-proof-" + Guid.NewGuid().ToString("N") + "@example.invalid"));
            Assert.Equal(IdentityCoherenceStatus.Mismatch, mismatch.Status);
            Assert.False(mismatch.AutodeskThreeLeggedMatch);

            var guard = sp.GetRequiredService<IIdentityOperationGuard>();
            var denied = await guard.EvaluateAsync(IdentityOperationKind.AutodeskThreeLeggedWrite);
            Assert.False(denied.Allowed);
        }

        // Restore observation snapshot (no logout).
        _ = await coherence.EvaluateAsync(
            new IdentityCoherenceEvaluateOptions(DisconnectGoogleOnMismatch: false));
    }
}
