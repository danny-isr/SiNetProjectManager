using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SiNet.Application.Billing;
using SiNet.Application.Identity;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>
/// Persists one current local billing decision per MasterPlan project id.
/// Does not query or write Replica / MasterPlan.
/// </summary>
public sealed class SqlBillingReviewDecisionService : IBillingReviewDecisionService, IBillingReviewDecisionStore
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IAuthorizationQueryService _authorization;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserProfileService? _profile;
    private readonly TimeProvider _time;

    public SqlBillingReviewDecisionService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IAuthorizationQueryService authorization,
        ICurrentUserContext currentUser,
        ICurrentUserProfileService? profile = null,
        TimeProvider? timeProvider = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _profile = profile;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<BillingReviewDecisionRecord>> GetByProjectIdsAsync(
        IReadOnlyList<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
            return [];

        var ids = projectIds.Distinct().ToList();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.BillingReviewDecisions
            .AsNoTracking()
            .Where(r => ids.Contains(r.ProjectId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task SaveAsync(
        BillingReviewDecisionWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProjectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "ProjectId must be a MasterPlan project identity.");

        if (request.DecisionType == BillingLocalDecisionType.NotNow
            && string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("NotNow requires a reason.", nameof(request));
        }

        await RequireWriteAsync(cancellationToken).ConfigureAwait(false);
        var actor = await ResolveActorAsync(cancellationToken).ConfigureAwait(false);
        var utcNow = _time.GetUtcNow().UtcDateTime;
        var reviewDate = request.ReviewAgainDate?.Date;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var useTransaction = db.Database.IsRelational();
        await using IDbContextTransaction? tx = useTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        var existing = await db.BillingReviewDecisions
            .SingleOrDefaultAsync(r => r.ProjectId == request.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.BillingReviewDecisions.Add(new BillingReviewDecision
            {
                ProjectId = request.ProjectId,
                DecisionType = request.DecisionType.ToString(),
                Reason = NormalizeReason(request.Reason),
                ReviewAgainDate = reviewDate,
                CreatedAtUtc = utcNow,
                CreatedByUserId = actor.UserId,
                CreatedByLogin = actor.Login,
                ObservedLatestBillId = request.ObservedLatestBillId,
                ObservedLatestBillStatusId = request.ObservedLatestBillStatusId,
                ObservedLastBillDate = request.ObservedLastBillDate,
                ObservedHoursSinceLastBill = request.ObservedHoursSinceLastBill,
                ObservedLastWorkDate = request.ObservedLastWorkDate
            });
        }
        else
        {
            ApplyUpdate(existing, request, actor, utcNow, reviewDate);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (tx is not null)
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (existing is null)
        {
            if (tx is not null)
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            await RetryAsUpdateAsync(request, actor, utcNow, reviewDate, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ClearAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(projectId), "ProjectId must be a MasterPlan project identity.");

        await RequireWriteAsync(cancellationToken).ConfigureAwait(false);
        var actor = await ResolveActorAsync(cancellationToken).ConfigureAwait(false);
        var utcNow = _time.GetUtcNow().UtcDateTime;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.BillingReviewDecisions
            .SingleOrDefaultAsync(r => r.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
            return;

        existing.ClearedAtUtc = utcNow;
        existing.ClearedByUserId = actor.UserId;
        existing.ClearedByLogin = actor.Login;
        existing.UpdatedAtUtc = utcNow;
        existing.UpdatedByUserId = actor.UserId;
        existing.UpdatedByLogin = actor.Login;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RetryAsUpdateAsync(
        BillingReviewDecisionWriteRequest request,
        Actor actor,
        DateTime utcNow,
        DateTime? reviewDate,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await db.BillingReviewDecisions
            .SingleAsync(r => r.ProjectId == request.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        ApplyUpdate(existing, request, actor, utcNow, reviewDate);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyUpdate(
        BillingReviewDecision existing,
        BillingReviewDecisionWriteRequest request,
        Actor actor,
        DateTime utcNow,
        DateTime? reviewDate)
    {
        existing.DecisionType = request.DecisionType.ToString();
        existing.Reason = NormalizeReason(request.Reason);
        existing.ReviewAgainDate = reviewDate;
        existing.UpdatedAtUtc = utcNow;
        existing.UpdatedByUserId = actor.UserId;
        existing.UpdatedByLogin = actor.Login;
        existing.ClearedAtUtc = null;
        existing.ClearedByUserId = null;
        existing.ClearedByLogin = null;
        existing.ObservedLatestBillId = request.ObservedLatestBillId;
        existing.ObservedLatestBillStatusId = request.ObservedLatestBillStatusId;
        existing.ObservedLastBillDate = request.ObservedLastBillDate;
        existing.ObservedHoursSinceLastBill = request.ObservedHoursSinceLastBill;
        existing.ObservedLastWorkDate = request.ObservedLastWorkDate;
    }

    private async Task RequireWriteAsync(CancellationToken cancellationToken)
    {
        if (!await _authorization
                .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.BillingRecordReviewDecision, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException("Management access is required to record a billing review decision.");
        }
    }

    private async Task<Actor> ResolveActorAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required to record a billing review decision.");
        var profile = _profile is null
            ? null
            : await _profile.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        var login = !string.IsNullOrWhiteSpace(profile?.LoginName)
            ? profile.LoginName
            : !string.IsNullOrWhiteSpace(profile?.DisplayName)
                ? profile.DisplayName
                : $"משתמש #{userId}";
        return new Actor(userId, login);
    }

    private static string? NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static BillingReviewDecisionRecord Map(BillingReviewDecision row) =>
        new(
            row.ProjectId,
            ParseType(row.DecisionType),
            row.Reason,
            row.ReviewAgainDate,
            row.CreatedAtUtc,
            row.CreatedByUserId,
            row.CreatedByLogin,
            row.UpdatedAtUtc,
            row.UpdatedByUserId,
            row.UpdatedByLogin,
            row.ClearedAtUtc,
            row.ClearedByUserId,
            row.ClearedByLogin,
            row.ObservedLatestBillId,
            row.ObservedLatestBillStatusId,
            row.ObservedLastBillDate,
            row.ObservedHoursSinceLastBill,
            row.ObservedLastWorkDate);

    private static BillingLocalDecisionType ParseType(string value) =>
        Enum.TryParse<BillingLocalDecisionType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Unknown billing decision type '{value}'.");

    private sealed record Actor(int UserId, string Login);
}
