using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email.Detail;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
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
        SystemCertificationEnvironment.AccLayer Acc);

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

        await using var readProvider = BuildReadOnly(target.ConnectionString);
        var dbFactory = readProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var marker = await SystemCertificationDatabaseMarker.VerifyAsync(dbFactory, cancellationToken);
        if (!marker.IsApproved)
        {
            return new WriteAuthorizationResult(null, marker.Violation);
        }

        var gmail = SystemCertificationEnvironment.TryResolveGmailLayer();
        var acc = SystemCertificationEnvironment.TryResolveAccLayer(gmail);
        var context = new SystemCertificationRunContext(target, target.OperatorUserId, gmail, acc);
        var provider = BuildWriteProvider(target, context);

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
        SystemCertificationRunContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ConnectionString);
        ArgumentNullException.ThrowIfNull(context);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSiNetLogging();
        services.AddSiNetSecrets();
        services.AddSiNetSql(target.ConnectionString);
        services.AddSiNetIdentitySql();
        services.AddSiNetAuthorizationSql();
        services.AddSiNetSystemSettingsSql();
        services.AddSiNetProcessBackbone();

        // Production email-detail composition — PRP must start through IEmailSuggestedActionExecutionService /
        // CreatePriceQuote, not a bypass around the real host seam.
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

        services.AddSingleton(context);
        return services.BuildServiceProvider();
    }

    /// <summary>Test seam for verifying production write composition without live SQL guards.</summary>
    internal static ServiceProvider BuildWriteProviderForTests(
        SystemCertificationEnvironment.Target target,
        SystemCertificationRunContext context) =>
        BuildWriteProvider(target, context);
}
