using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email.Detail;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using System.IO;
using System.Security.Principal;
using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

public sealed class SystemCertificationHostTests
{
    [Fact]
    public async Task TryCreateAuthorizedWriteHostAsync_returns_skip_reason_when_tier_is_not_enabled()
    {
        var previous = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.EnabledEnv);
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.EnabledEnv, null);

        try
        {
            var result = await SystemCertificationHost.TryCreateAuthorizedWriteHostAsync(CancellationToken.None);

            Assert.Null(result.Host);
            Assert.NotNull(result.Violation);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.EnabledEnv, previous);
        }
    }

    [Fact]
    public void Write_provider_registers_production_email_suggested_action_execution_service()
    {
        var target = new SystemCertificationEnvironment.Target(
            IsEnabled: true,
            SkipReason: null,
            Violation: null,
            ConnectionString: "Server=.;Database=SystemCertificationTest;Trusted_Connection=True;",
            ServerName: ".",
            DatabaseName: "SystemCertificationTest",
            WindowsIdentityName: Environment.UserName,
            OperatorUserId: 1);

        var context = new SystemCertificationHost.SystemCertificationRunContext(
            target,
            1,
            new SystemCertificationEnvironment.GmailLayer(false, "not requested", null, null),
            new SystemCertificationEnvironment.AccLayer(false, "not requested", null, null, null),
            AccGuard: null);

        using var provider = SystemCertificationHost.BuildWriteProviderForTests(target, context);

        var execution = provider.GetRequiredService<IEmailSuggestedActionExecutionService>();
        Assert.NotNull(execution);
    }

    [Fact]
    public async Task Database_marker_guard_fails_when_marker_row_is_absent()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var factory = new InMemoryDbFactory(options);
        var marker = await SystemCertificationDatabaseMarker.VerifyAsync(factory, CancellationToken.None);

        Assert.False(marker.IsApproved);
        Assert.NotNull(marker.Violation);
    }

    [Fact]
    public async Task TryCreateAuthorizedWriteHostAsync_fails_when_gmail_layer_is_requested_but_invalid()
    {
        var previousEnabled = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.EnabledEnv);
        var previousGmail = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.GmailEnabledEnv);
        var previousAccount = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.GmailAccountEnv);
        var previousSql = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.SqlConnectionEnv);
        var previousServers = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.AllowedServersEnv);
        var previousDatabases = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.AllowedDatabasesEnv);
        var previousUsers = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.AllowedWindowsUsersEnv);
        var previousUserId = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.OperatorUserIdEnv);

        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.EnabledEnv, "1");
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.GmailEnabledEnv, "1");
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.GmailAccountEnv, null);
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.SqlConnectionEnv, "Server=.;Database=x;Trusted_Connection=True;");
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.AllowedServersEnv, ".");
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.AllowedDatabasesEnv, "x");
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.AllowedWindowsUsersEnv, WindowsIdentity.GetCurrent().Name);
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.OperatorUserIdEnv, "1");

        try
        {
            var result = await SystemCertificationHost.TryCreateAuthorizedWriteHostAsync(CancellationToken.None);

            Assert.Null(result.Host);
            Assert.Contains(SystemCertificationEnvironment.GmailAccountEnv, result.Violation, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.EnabledEnv, previousEnabled);
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.GmailEnabledEnv, previousGmail);
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.GmailAccountEnv, previousAccount);
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.SqlConnectionEnv, previousSql);
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.AllowedServersEnv, previousServers);
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.AllowedDatabasesEnv, previousDatabases);
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.AllowedWindowsUsersEnv, previousUsers);
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.OperatorUserIdEnv, previousUserId);
        }
    }

    [Fact]
    public async Task Prp_scenario_blocks_when_live_gate_is_off()
    {
        var previous = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.PrpLiveEnabledEnv);
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PrpLiveEnabledEnv, null);

        try
        {
            var target = new SystemCertificationEnvironment.Target(
                IsEnabled: true,
                SkipReason: null,
                Violation: null,
                ConnectionString: "Server=.;Database=SystemCertificationTest;Trusted_Connection=True;",
                ServerName: ".",
                DatabaseName: "SystemCertificationTest",
                WindowsIdentityName: Environment.UserName,
                OperatorUserId: 1);

            var context = new SystemCertificationHost.SystemCertificationRunContext(
                target,
                1,
                new SystemCertificationEnvironment.GmailLayer(true, null, null, "test@example.com"),
                new SystemCertificationEnvironment.AccLayer(false, "not requested", null, null, null),
                AccGuard: null);

            using var provider = SystemCertificationHost.BuildWriteProviderForTests(target, context);
            await using var host = new SystemCertificationHost.AuthorizedWriteHost(
                provider,
                target,
                new SystemCertificationDatabaseMarker.Result(true, null, "DEV"),
                context);

            var evidence = SystemCertificationEvidence.Create(Path.GetTempPath());
            var scenario = new Scenarios.SystemCertificationPrpScenario();
            await scenario.RunAsync(host, evidence, CancellationToken.None);

            Assert.Equal(SystemCertificationEvidence.NotCertifiedVerdict, evidence.Verdict);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PrpLiveEnabledEnv, previous);
        }
    }

    private sealed class InMemoryDbFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SiNetSQLDbContext(options));
    }
}
