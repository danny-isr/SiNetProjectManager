using Microsoft.EntityFrameworkCore;
using SiNet.Application.Billing;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.Billing;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingReviewDecisionServiceTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unique_ProjectId_index_exists_and_has_no_false_project_fk()
    {
        var options = InMemoryOptions();
        using var db = new SiNetSQLDbContext(options);
        var entity = db.Model.FindEntityType(typeof(BillingReviewDecision));
        Assert.NotNull(entity);
        Assert.Equal("BillingReviewDecision", entity.GetTableName());
        Assert.Contains(
            entity.GetIndexes(),
            i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["ProjectId"]));
        Assert.Empty(entity.GetForeignKeys());
    }

    [Fact]
    public async Task PrepareBill_persists_observed_replica_evidence()
    {
        var harness = CreateHarness(allowWrite: true);
        var request = Write(5905, BillingLocalDecisionType.PrepareBill, reason: null, observedBillId: 11);

        await harness.Service.SaveAsync(request);

        var stored = Assert.Single(await harness.Service.GetByProjectIdsAsync([5905]));
        Assert.Equal(BillingLocalDecisionType.PrepareBill, stored.DecisionType);
        Assert.Null(stored.Reason);
        Assert.Equal(11, stored.ObservedLatestBillId);
        Assert.Equal(2, stored.ObservedLatestBillStatusId);
        Assert.Equal(12m, stored.ObservedHoursSinceLastBill);
        Assert.Equal(7, stored.CreatedByUserId);
        Assert.Equal("manager.login", stored.CreatedByLogin);
        Assert.Null(stored.ClearedAtUtc);
        Assert.Null(stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task NotNow_requires_reason()
    {
        var harness = CreateHarness(allowWrite: true);
        var request = Write(5893, BillingLocalDecisionType.NotNow, reason: "  ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => harness.Service.SaveAsync(request));
        Assert.Contains("NotNow requires a reason", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await harness.Service.GetByProjectIdsAsync([5893]));
    }

    [Fact]
    public async Task Second_save_updates_same_row_instead_of_duplicating()
    {
        var harness = CreateHarness(allowWrite: true);
        await harness.Service.SaveAsync(Write(3611, BillingLocalDecisionType.PrepareBill, observedBillId: 1));
        await harness.Service.SaveAsync(Write(
            3611,
            BillingLocalDecisionType.NotNow,
            reason: "לחכות לסקר",
            reviewAgain: new DateTime(2026, 10, 1),
            observedBillId: 1));

        await using var db = new SiNetSQLDbContext(harness.Options);
        Assert.Equal(1, await db.BillingReviewDecisions.CountAsync());
        var row = Assert.Single(await db.BillingReviewDecisions.ToListAsync());
        Assert.Equal(3611, row.ProjectId);
        Assert.Equal(nameof(BillingLocalDecisionType.NotNow), row.DecisionType);
        Assert.Equal("לחכות לסקר", row.Reason);
        Assert.Equal(7, row.CreatedByUserId);
        Assert.Equal(7, row.UpdatedByUserId);
        Assert.NotNull(row.UpdatedAtUtc);
        Assert.Null(row.ClearedAtUtc);
    }

    [Fact]
    public async Task Clear_stamps_cleared_audit_and_leaves_the_row()
    {
        var harness = CreateHarness(allowWrite: true);
        await harness.Service.SaveAsync(Write(100, BillingLocalDecisionType.PrepareBill));
        await harness.Service.ClearAsync(100);

        var stored = Assert.Single(await harness.Service.GetByProjectIdsAsync([100]));
        Assert.NotNull(stored.ClearedAtUtc);
        Assert.Equal(7, stored.ClearedByUserId);
        Assert.Equal("manager.login", stored.ClearedByLogin);
    }

    [Fact]
    public async Task Employee_cannot_write()
    {
        var harness = CreateHarness(allowWrite: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => harness.Service.SaveAsync(Write(5905, BillingLocalDecisionType.PrepareBill)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => harness.Service.ClearAsync(5905));
        await using var db = new SiNetSQLDbContext(harness.Options);
        Assert.Equal(0, await db.BillingReviewDecisions.CountAsync());
    }

    [Fact]
    public async Task Management_can_write()
    {
        var harness = CreateHarness(allowWrite: true);
        await harness.Service.SaveAsync(Write(5905, BillingLocalDecisionType.PrepareBill));
        Assert.Single(await harness.Service.GetByProjectIdsAsync([5905]));
    }

    [Fact]
    public async Task Unauthenticated_user_cannot_write()
    {
        var harness = CreateHarness(allowWrite: true, userId: null);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.SaveAsync(Write(5905, BillingLocalDecisionType.PrepareBill)));
    }

    private static Harness CreateHarness(bool allowWrite, int? userId = 7)
    {
        var options = InMemoryOptions();
        var service = new SqlBillingReviewDecisionService(
            new StubDbContextFactory(options),
            new StubAuth(allowWrite),
            new StubUser(userId),
            new StubProfile(userId),
            new FrozenClock(UtcNow));
        return new Harness(options, service);
    }

    private static DbContextOptions<SiNetSQLDbContext> InMemoryOptions() =>
        new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static BillingReviewDecisionWriteRequest Write(
        int projectId,
        BillingLocalDecisionType type,
        string? reason = null,
        DateTime? reviewAgain = null,
        int? observedBillId = null) =>
        new(
            projectId,
            type,
            reason,
            reviewAgain,
            observedBillId,
            observedBillId is null ? null : 2,
            observedBillId is null ? null : new DateTime(2026, 6, 1),
            12m,
            new DateTime(2026, 9, 1));

    private sealed record Harness(
        DbContextOptions<SiNetSQLDbContext> Options,
        SqlBillingReviewDecisionService Service);

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class StubAuth(bool allow) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default) =>
            Task.FromResult(allow && requiredRole <= AppRole.Management);

        public Task<bool> CanCurrentUserAccessFeatureAsync(
            string featureCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(allow && featureCode == AppFeatureCodes.BillingRecordReviewDecision);
    }

    private sealed class StubUser(int? userId) : ICurrentUserContext
    {
        public int? UserId => userId;
    }

    private sealed class StubProfile(int? userId) : ICurrentUserProfileService
    {
        public Task<CurrentUserProfileDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            if (userId is not int id)
                return Task.FromResult<CurrentUserProfileDto?>(null);
            return Task.FromResult<CurrentUserProfileDto?>(new CurrentUserProfileDto(
                id,
                "Manager",
                "manager.login",
                AppRole.Management,
                IsActive: true));
        }
    }

    private sealed class FrozenClock(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
