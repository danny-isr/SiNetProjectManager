using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Infrastructure.Sql;
using SiNetSQL.Data;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Single DI composition root for the certification tier. Scenarios and preflight must use this instead of
/// hand-rolling service graphs, so a probe that resolves cleanly is evidence that a scenario will too.
/// </summary>
internal static class SystemCertificationHost
{
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
    /// Write graph for workflow and email scenarios. Gmail and ACC modules are included only when their
    /// layer flags are set in <see cref="SystemCertificationEnvironment"/> — the same flags preflight
    /// validates.
    /// </summary>
    public static ServiceProvider BuildWrite(
        SystemCertificationEnvironment.Target target,
        int operatorUserId)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ConnectionString);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSiNetSql(target.ConnectionString);
        services.AddSiNetIdentitySql();
        services.AddSiNetAuthorizationSql();
        services.AddSiNetSystemSettingsSql();
        services.AddSiNetProcessBackbone();

        var gmail = SystemCertificationEnvironment.TryResolveGmailLayer();
        if (gmail.IsEnabled && gmail.Violation is null)
        {
            // Gmail registration is deferred until live scenarios need it. Preflight only validates env.
        }

        var acc = SystemCertificationEnvironment.TryResolveAccLayer(gmail);
        if (acc.IsEnabled && acc.Violation is null)
        {
            // ACC registration is deferred until live scenarios need it. Preflight only validates env.
        }

        services.AddSingleton(new SystemCertificationRunContext(
            target,
            operatorUserId,
            gmail,
            acc));

        return services.BuildServiceProvider();
    }

    internal sealed record SystemCertificationRunContext(
        SystemCertificationEnvironment.Target Target,
        int OperatorUserId,
        SystemCertificationEnvironment.GmailLayer Gmail,
        SystemCertificationEnvironment.AccLayer Acc);
}
