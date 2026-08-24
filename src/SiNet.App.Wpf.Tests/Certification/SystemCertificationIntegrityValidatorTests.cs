using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

public sealed class SystemCertificationIntegrityValidatorTests
{
    [Fact]
    public void WhenWaiverReasonIsMissingThenUseWaiversThrows()
    {
        var validator = CreateValidator();
        var waiver = new SystemCertificationIntegrityValidator.Waiver(
            "DuplicateActiveTrack",
            "project:1/definition:2",
            Reason: "",
            ApprovedBy: "operator");

        var ex = Assert.Throws<ArgumentException>(() => validator.UseWaivers([waiver]));
        Assert.Contains("Reason", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenWaiverApproverIsMissingThenUseWaiversThrows()
    {
        var validator = CreateValidator();
        var waiver = new SystemCertificationIntegrityValidator.Waiver(
            "DuplicateActiveTrack",
            "project:1/definition:2",
            Reason: "restored DEV baseline",
            ApprovedBy: " ");

        var ex = Assert.Throws<ArgumentException>(() => validator.UseWaivers([waiver]));
        Assert.Contains("ApprovedBy", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenWaiverIsCompleteThenUseWaiversAcceptsIt()
    {
        var validator = CreateValidator();
        validator.UseWaivers([
            new SystemCertificationIntegrityValidator.Waiver(
                "DuplicateActiveTrack",
                "project:1/definition:2",
                "restored DEV baseline",
                "AzureAD\\operator"),
        ]);
    }

    private static SystemCertificationIntegrityValidator CreateValidator()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var factory = new InMemoryDbFactory(options);
        return new SystemCertificationIntegrityValidator(factory);
    }

    private sealed class InMemoryDbFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SiNetSQLDbContext(options));
    }
}
