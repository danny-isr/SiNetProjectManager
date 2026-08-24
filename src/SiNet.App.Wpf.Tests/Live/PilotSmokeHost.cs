using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Infrastructure.AccBootstrap;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.AutodeskLocal;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// The single service graph for the L4W tier. Both the write run and the read-only probe build it,
/// so a probe that resolves cleanly is evidence that the write run will too — the divergence between
/// two hand-rolled graphs is precisely what a pre-flight is supposed to rule out.
/// <para>
/// Mirrors <c>StandaloneHostServiceCollectionExtensions</c> for the parts under test, with two
/// deliberate departures documented in <c>docs/TEST_STRATEGY.md</c> §4W.2: ACC writes are decorated
/// by <see cref="PilotSmokeAccGuard"/>, and the inbox bootstrap is pinned to the local executor.
/// </para>
/// </summary>
internal static class PilotSmokeHost
{
    public static Microsoft.Extensions.DependencyInjection.ServiceProvider Build(
        string connectionString,
        PilotSmokeEnvironment.AccTier accTier,
        PilotSmokeAccGuard guard,
        bool includeProcessBackbone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(accTier);
        ArgumentNullException.ThrowIfNull(guard);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSiNetLogging();
        services.AddSiNetSecrets();
        services.AddSiNetSql(connectionString);
        services.AddSiNetIdentitySql();
        services.AddSiNetAuthorizationSql();
        services.AddSiNetSystemSettingsSql();

        if (includeProcessBackbone)
        {
            services.AddSiNetProcessBackbone();
        }

        services.AddSiNetGoogle(static options =>
        {
            options.ApplicationName = "SiNet.PilotSmoke";
            // Never open a browser from an automated run.
            options.AllowInteractiveSignIn = false;
            // Must match src/SiNet.App.Wpf/appsettings.json — the app persists tokens here; the
            // module default ("sinet-google-token") is a different folder and silent restore fails
            // even when the UI shows Gmail connected.
            options.TokenStorePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiNet",
                "google-token");
        });

        services.AddSiNetEmailReadSql();
        services.AddSiNetEmailWriteSql();

        if (!accTier.IsEnabled)
        {
            PilotSmokeAccGuard.AssertNoAccWritePortsRegistered(services);
            return services.BuildServiceProvider();
        }

        // The vault token provider is host-level wiring in production
        // (StandaloneHostServiceCollectionExtensions); without it the local inbox executor cannot be
        // constructed at all.
        services.AddSiNetAutodeskVaultTokenProvider();
        services.AddSiNetAutodesk();
        services.AddSiNetAutodeskLocalSql();
        services.AddSiNetAccInboxBootstrapLocal();
        services.AddSiNetAccProjectProvisioning();
        services.AddSiNetEmailAccSql();
        services.AddSiNetEmailDetailSql();
        services.AddSiNetFilingServices();

        // The Office Inbox target is the one ACC decision this process does not fully own: in Remote
        // mode AccService resolves InboxProjectName from ITS OWN database, so the override written
        // into the smoke database would be ignored. Bind the inbox bootstrap to the in-process local
        // executor so the setting under our control is the one that decides.
        services.AddSingleton<IAccInboxBootstrapService, LocalOnlyInboxBootstrap>();

        guard.Decorate(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Forces the local in-process inbox bootstrap. Refuses rather than silently falling back to the
    /// remote AccService path, whose <c>InboxProjectName</c> comes from a database this run does not
    /// control (see <c>docs/ENVIRONMENTS.md</c> §5.1.1).
    /// </summary>
    internal sealed class LocalOnlyInboxBootstrap(IAccInboxBootstrapLocalExecutor? local)
        : IAccInboxBootstrapService
    {
        public Task<AccInboxBootstrapResult> EnsureAsync(CancellationToken cancellationToken = default)
        {
            if (local is null)
            {
                throw new InvalidOperationException(
                    "IAccInboxBootstrapLocalExecutor is not registered, so the smoke cannot guarantee "
                    + "that the InboxProjectName it wrote is the one ACC will use. Refusing to fall "
                    + "back to the remote AccService path.");
            }

            return local.EnsureAsync(cancellationToken);
        }
    }
}
