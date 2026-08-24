using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email.Detail;
using SiNetSQL.Data;
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
            new SystemCertificationEnvironment.AccLayer(false, "not requested", null, null, null));

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

    private sealed class InMemoryDbFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SiNetSQLDbContext(options));
    }
}
