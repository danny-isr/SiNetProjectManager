using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Email.Detail;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.AccBootstrap;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.AutodeskLocal;
using SiNet.App.Wpf.Tests.Live;
using SiNetSQL.Data;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Thrown when a write scenario attempts to run without passing every guard independently of test ordering.
/// </summary>
internal sealed class SystemCertificationWriteGuardException(string message) : Exception(message);

/// <summary>
/// Single DI composition root for the certification tier. Every write scenario must obtain its provider through
/// <see cref="CreateAuthorizedWriteHostAsync"/> so target env, DB marker and production seams are proven
/// again even if preflight already passed in an earlier test.
/// </summary>
internal static class SystemCertificationHost
{
    internal sealed record AuthorizedWriteHost(
        ServiceProvider Provider,
        SystemCertificationEnvironment.Target Target,
        SystemCertificationDatabaseMarker.Result Marker,
        SystemCertificationRunContext Context) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (Provider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                Provider.Dispose();
            }
        }
    }

    internal sealed record WriteAuthorizationResult(
        AuthorizedWriteHost? Host,
        string? Violation);

    internal sealed record SystemCertificationRunContext(
        SystemCertificationEnvironment.Target Target,
        int OperatorUserId,
        SystemCertificationEnvironment.GmailLayer Gmail,
        SystemCertificationEnvironment.AccLayer Acc,
        PilotSmokeAccGuard? AccGuard);

    /// <summary>
    /// Read-only graph: SQL and settings only. Used by preflight — no Gmail, ACC, or workflow writes.
    /// </summary>
    public static ServiceProvider BuildReadOnly(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSiNetSql(connectionString);
        services.AddSiNetSystemSettingsSql();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Central write gate. Resolves the env target, verifies the in-database DEV marker, then builds the
    /// production write graph including the email suggested-action seam used by PRP CreatePriceQuote.
    /// </summary>
    public static async Task<WriteAuthorizationResult> TryCreateAuthorizedWriteHostAsync(
        CancellationToken cancellationToken = default)
    {
        var target = SystemCertificationEnvironment.TryResolveTarget();
        if (!target.IsEnabled)
        {
            return new WriteAuthorizationResult(null, target.SkipReason ?? "certification tier is not enabled");
        }

        if (target.Violation is not null)
        {
            return new WriteAuthorizationResult(null, target.Violation);
        }

        if (!target.IsAuthorised || target.ConnectionString is null)
        {
            return new WriteAuthorizationResult(null, "target resolution did not produce an authorised connection.");
        }

        var gmail = SystemCertificationEnvironment.TryResolveGmailLayer();
        if (SystemCertificationEnvironment.IsLayerRequested(SystemCertificationEnvironment.GmailEnabledEnv)
            && gmail.Violation is not null)
        {
            return new WriteAuthorizationResult(
                null,
                $"Gmail layer requested ({SystemCertificationEnvironment.GmailEnabledEnv}=1) but invalid: "
                + gmail.Violation);
        }

        var acc = SystemCertificationEnvironment.TryResolveAccLayer(gmail);
        if (SystemCertificationEnvironment.IsLayerRequested(SystemCertificationEnvironment.AccEnabledEnv)
            && acc.Violation is not null)
        {
            return new WriteAuthorizationResult(
                null,
                $"ACC layer requested ({SystemCertificationEnvironment.AccEnabledEnv}=1) but invalid: "
                + acc.Violation);
        }

        await using var readProvider = BuildReadOnly(target.ConnectionString);
        var dbFactory = readProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var marker = await SystemCertificationDatabaseMarker.VerifyAsync(dbFactory, cancellationToken);
        if (!marker.IsApproved)
        {
            return new WriteAuthorizationResult(null, marker.Violation);
        }

        var context = new SystemCertificationRunContext(target, target.OperatorUserId, gmail, acc, null);
        var provider = BuildWriteProvider(target, context, out var accGuard);
        context = context with { AccGuard = accGuard };

        try
        {
            _ = provider.GetRequiredService<IEmailSuggestedActionExecutionService>();
        }
        catch (Exception ex)
        {
            provider.Dispose();
            return new WriteAuthorizationResult(
                null,
                "write host is missing IEmailSuggestedActionExecutionService: " + ex.Message);
        }

        return new WriteAuthorizationResult(
            new AuthorizedWriteHost(provider, target, marker, context),
            null);
    }

    /// <summary>
    /// Same as <see cref="TryCreateAuthorizedWriteHostAsync"/> but throws when any guard fails.
    /// </summary>
    public static async Task<AuthorizedWriteHost> CreateAuthorizedWriteHostAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await TryCreateAuthorizedWriteHostAsync(cancellationToken);
        if (result.Violation is not null || result.Host is null)
        {
            throw new SystemCertificationWriteGuardException(result.Violation ?? "write authorization failed");
        }

        return result.Host;
    }

    private static ServiceProvider BuildWriteProvider(
        SystemCertificationEnvironment.Target target,
        SystemCertificationRunContext context,
        out PilotSmokeAccGuard? accGuard)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ConnectionString);
        ArgumentNullException.ThrowIfNull(context);

        accGuard = null;
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSiNetLogging();
        services.AddSiNetSecrets();
        services.AddSiNetSql(target.ConnectionString);
        services.AddSiNetIdentitySql();
        services.AddSiNetAuthorizationSql();
        services.AddSiNetSystemSettingsSql();
        services.AddSiNetProcessBackbone();

        services.AddSiNetEmailReadSql();
        services.AddSiNetEmailWriteSql();
        services.AddSiNetEmailDetailSql();
        services.AddTransient<IOpenQuoteProjectDecisionService, OpenQuoteProjectDecisionService>();

        if (context.Gmail.IsEnabled && context.Gmail.Violation is null)
        {
            services.AddSiNetGoogle(static options =>
            {
                options.ApplicationName = "SiNet.SystemCertification";
                options.AllowInteractiveSignIn = false;
                options.TokenStorePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SiNet",
                    "google-token");
            });
        }

        if (context.Acc.IsEnabled && context.Acc.Violation is null)
        {
            accGuard = new PilotSmokeAccGuard();
            services.AddSingleton(accGuard);
            services.AddSiNetAutodeskVaultTokenProvider();
            services.AddSiNetAutodesk();
            services.AddSiNetAutodeskLocalSql();
            services.AddSiNetAccInboxBootstrapLocal();
            services.AddSiNetAccProjectProvisioning();
            services.AddSiNetEmailAccSql();
            services.AddSiNetFilingServices();
            services.AddSingleton<IAccInboxBootstrapService, PilotSmokeHost.LocalOnlyInboxBootstrap>();
            accGuard.Decorate(services);
        }
        else if (!SystemCertificationEnvironment.IsLayerRequested(SystemCertificationEnvironment.AccEnabledEnv))
        {
            PilotSmokeAccGuard.AssertNoAccWritePortsRegistered(services);
        }

        services.AddSingleton(context with { AccGuard = accGuard });
        return services.BuildServiceProvider();
    }

    /// <summary>Test seam for verifying production write composition without live SQL guards.</summary>
    internal static ServiceProvider BuildWriteProviderForTests(
        SystemCertificationEnvironment.Target target,
        SystemCertificationRunContext context)
    {
        var provider = BuildWriteProvider(target, context, out _);
        return provider;
    }
}
