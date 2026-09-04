using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Common;
using SiNet.Application.Identity;
using SiNet.Infrastructure.AccBootstrap;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
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
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSiNetLogging();
        services.AddSiNetSecrets();
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
            var denied = await guard.EvaluateAsync(
                IdentityOperationKind.AutodeskThreeLeggedWrite,
                new IdentityOperationContext(
                    AutodeskThreeLeggedEmail: "mismatch-proof-" + Guid.NewGuid().ToString("N") + "@example.invalid"));
            Assert.False(denied.Allowed);
        }

        // Restore observation snapshot (no logout).
        _ = await coherence.EvaluateAsync(
            new IdentityCoherenceEvaluateOptions(DisconnectGoogleOnMismatch: false));
    }

    /// <summary>
    /// Controlled live ACC membership readback for an existing mapped SiNet project (PRP #80 → ProjectId 3213).
    /// Does not mutate SIUser, Google session, ACC membership, or SQL cache.
    /// </summary>
    [LiveFact]
    public async Task Live_acc_membership_readback_for_active_mapped_project()
    {
        var cs = LiveEnvironment.TryResolveSqlConnectionString();
        Assert.False(string.IsNullOrWhiteSpace(cs));

        // Prefer env override; otherwise use PRP #80's project which already has ProjectAccMapping in DEV.
        var siProjectId = 3213;
        var fromEnv = Environment.GetEnvironmentVariable("SINET_LIVE_IDENTITY_SIPROJECT_ID");
        if (int.TryParse(fromEnv, out var parsed) && parsed > 0)
        {
            siProjectId = parsed;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AccService:BaseUrl"] = LiveEnvironment.AccBaseUrl,
                })
                .Build());
        services.AddSiNetLogging();
        services.AddSiNetSecrets();
        services.AddSiNetSql(cs!);
        services.AddSiNetIdentitySql();
        services.AddSiNetAutodeskVaultTokenProvider();
        services.AddSiNetAutodesk();
        services.AddSiNetAccProjectProvisioning();
        services.AddSiNetGoogle(static options =>
        {
            options.ApplicationName = "SiNet.IdentityAccLiveProof";
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
        Assert.False(string.IsNullOrWhiteSpace(auth.Profile!.Email));

        var google = sp.GetRequiredService<IConnectorAuthService>();
        _ = await google.TryRestoreSessionAsync();

        var resolver = sp.GetRequiredService<IAccProjectIdResolver>();
        var accProjectId = await resolver.ResolveAccProjectIdAsync(siProjectId);
        Assert.False(string.IsNullOrWhiteSpace(accProjectId));

        var probe = sp.GetRequiredService<IAccHumanMembershipProbe>();
        Assert.IsType<AccHumanMembershipProbe>(probe);

        var membership = await probe.ProbeAsync(
            accProjectId,
            auth.Profile.Email!,
            allowReconcile: false);
        Assert.NotNull(membership);
        Assert.True(membership!.ProbeSucceeded, membership.FailureReason);
        Assert.Equal(
            auth.Profile.Email!.Trim(),
            membership.ExpectedEmail,
            ignoreCase: true);

        var coherence = sp.GetRequiredService<IIdentityCoherenceService>();
        var snap = await coherence.EvaluateAsync(new IdentityCoherenceEvaluateOptions(
            DisconnectGoogleOnMismatch: false,
            SiProjectId: siProjectId,
            HasActiveProject: true,
            AllowAccMembershipReconcile: false));

        Assert.Equal(auth.Profile.UserId, snap.SiUserId);
        Assert.Equal(siProjectId, snap.SiProjectId);
        Assert.Equal(accProjectId, snap.AccProjectId, ignoreCase: true);
        Assert.True(snap.AccRelevant);
        Assert.Equal(membership.IsMember, snap.AccMembershipMatch);
        Assert.Equal(membership.MatchedMemberEmail, snap.AccMembershipEmail, ignoreCase: true);

        // Emit operator-visible proof lines (no secrets).
        Console.WriteLine($"LIVE_IDENTITY SIUser.Id={snap.SiUserId}");
        Console.WriteLine($"LIVE_IDENTITY SIUser.LoginName={snap.SiUserLoginName}");
        Console.WriteLine($"LIVE_IDENTITY SIUser.Email={snap.SiUserEmail}");
        Console.WriteLine($"LIVE_IDENTITY GoogleEmail={snap.GoogleEmail}");
        Console.WriteLine($"LIVE_IDENTITY GoogleMatch={snap.GoogleMatch}");
        Console.WriteLine($"LIVE_IDENTITY SiProjectId={snap.SiProjectId}");
        Console.WriteLine($"LIVE_IDENTITY AccProjectId={snap.AccProjectId}");
        Console.WriteLine($"LIVE_IDENTITY AccMatchedMemberEmail={snap.AccMembershipEmail}");
        Console.WriteLine($"LIVE_IDENTITY AccMatch={snap.AccMembershipMatch}");
        Console.WriteLine($"LIVE_IDENTITY AccAccessLevel={snap.AccAccessLevel}");
        Console.WriteLine($"LIVE_IDENTITY OverallIdentityStatus={snap.Status}");
        Console.WriteLine($"LIVE_IDENTITY Footer={IdentityStatusDisplay.FormatFooter(snap)}");

        if (membership.IsMember)
        {
            Assert.True(snap.GoogleMatch is true);
            Assert.Equal(IdentityCoherenceStatus.Match, snap.Status);
            Assert.Contains("זהות: תקינה", IdentityStatusDisplay.FormatFooter(snap), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(IdentityCoherenceStatus.Mismatch, snap.Status);
            Assert.DoesNotContain("זהות: תקינה", IdentityStatusDisplay.FormatFooter(snap), StringComparison.Ordinal);
        }

        var guard = sp.GetRequiredService<IIdentityOperationGuard>();
        var writeDecision = await guard.EvaluateAsync(
            IdentityOperationKind.AccFileWrite,
            IdentityOperationContext.ForSiProject(siProjectId));
        Assert.Equal(membership.IsMember, writeDecision.Allowed);
    }
}
