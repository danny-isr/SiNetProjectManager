using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Explicit JobType taxonomy reconciliation for Review and Opinion.
/// This is not part of application startup and is not the DEBUG DevTools seed.
/// Dry-run reports conflicts and writes nothing. Apply uses the same rules as the general seed.
/// </summary>
public static class CanonicalJobTypeReconciliation
{
    public static async Task<CanonicalJobTypeReconciliationReport> PreviewAsync(
        SiNetSQLDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var jobTypes = await db.JobTypes.AsNoTracking()
            .Where(j => j.Title != null)
            .Select(j => new { j.Id, j.Title })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var canonical = jobTypes.FirstOrDefault(j => j.Title == CanonicalJobTypeSeed.ReviewJobTypeTitle);
        var legacy = jobTypes
            .Where(j => CanonicalJobTypeSeed.LegacyReviewJobTypeTitles.Contains(j.Title!, StringComparer.Ordinal))
            .ToList();
        var opinion = jobTypes.FirstOrDefault(j => j.Title == CanonicalJobTypeSeed.OpinionJobTypeTitle);
        var conflicts = new List<string>();
        conflicts.AddRange(await CanonicalJobTypeSeed
            .DescribePlannedReviewConflictsAsync(db, canonical?.Id, legacy.Select(j => j.Id).ToArray(), ct)
            .ConfigureAwait(false));
        conflicts.AddRange(await CanonicalJobTypeSeed
            .DescribeMissingWorkflowPrerequisitesAsync(db, ct)
            .ConfigureAwait(false));

        return new CanonicalJobTypeReconciliationReport(
            canonical?.Id,
            legacy.Select(j => j.Id).ToArray(),
            opinion?.Id,
            conflicts);
    }

    /// <summary>
    /// Read-only startup check. It never writes. A non-empty result means the
    /// workflow template screen can look empty until the one-time reconciler runs.
    /// </summary>
    public static async Task<IReadOnlyList<string>> DescribeRuntimeGapsAsync(
        SiNetSQLDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var gaps = new List<string>();
        await AddGapAsync(db, gaps, CanonicalJobTypeSeed.ReviewJobTypeTitle, WorkflowCodes.Review, ReviewStageCodes.ProfessionalReview, ct).ConfigureAwait(false);
        await AddGapAsync(db, gaps, CanonicalJobTypeSeed.OpinionJobTypeTitle, WorkflowCodes.Opinion, OpinionStageCodes.ReceiveMaterial, ct).ConfigureAwait(false);
        return gaps;
    }

    private static async Task AddGapAsync(
        SiNetSQLDbContext db,
        List<string> gaps,
        string jobTypeTitle,
        string workflowCode,
        string stageCode,
        CancellationToken ct)
    {
        var jobTypeId = await db.JobTypes.AsNoTracking()
            .Where(j => j.Title == jobTypeTitle)
            .Select(j => (int?)j.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (jobTypeId is null)
        {
            gaps.Add($"חסר סוג עבודה '{jobTypeTitle}'. תבניות התהליך לא יוצגו עד להרצת CanonicalJobTypeReconcile.");
            return;
        }

        var workflow = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Code == workflowCode && d.IsActive)
            .Select(d => new { d.Id })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (workflow is null)
        {
            gaps.Add($"הגדרת התהליך '{workflowCode}' חסרה או אינה פעילה.");
            return;
        }

        var enabled = await db.ProjectTypeWorkflowDefinitions.AsNoTracking()
            .AnyAsync(m => m.ProjectTypeId == jobTypeId && m.WorkflowDefinitionId == workflow.Id && m.IsEnabled, ct)
            .ConfigureAwait(false);
        if (!enabled)
            gaps.Add($"לסוג העבודה '{jobTypeTitle}' אין מיפוי מאופשר אל '{workflowCode}'.");

        var stageId = await db.WorkflowStageDefinitions.AsNoTracking()
            .Where(s => s.WorkflowDefinitionId == workflow.Id && s.Code == stageCode)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (stageId is null)
        {
            gaps.Add($"השלב '{stageCode}' חסר בתהליך '{workflowCode}'.");
            return;
        }

        var templates = await db.WorkflowStageTasks.AsNoTracking()
            .CountAsync(t => t.StageDefinitionId == stageId && t.IsActive, ct)
            .ConfigureAwait(false);
        if (templates == 0)
            gaps.Add($"לשלב '{stageCode}' אין תבנית משימה פעילה. המסך יישאר בלי תבניות.");
    }

    public static async Task ApplyAsync(SiNetSQLDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var preview = await PreviewAsync(db, ct).ConfigureAwait(false);
        if (preview.Conflicts.Count > 0)
        {
            throw new InvalidOperationException(
                "Canonical JobType reconciliation stopped. No rows were changed. "
                + string.Join(" ", preview.Conflicts));
        }

        var relational = db.Database.IsRelational() && db.Database.CurrentTransaction is null;
        await using var transaction = relational
            ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;
        try
        {
            await CanonicalJobTypeSeed.NormalizeJobTypesAsync(db, ct).ConfigureAwait(false);
            await CanonicalJobTypeSeed.EnsureWorkflowMappingsAsync(db, ct).ConfigureAwait(false);
            await CanonicalJobTypeSeed.EnsureStageProfilesAsync(db, ct).ConfigureAwait(false);
            if (transaction is not null)
                await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}

public sealed record CanonicalJobTypeReconciliationReport(
    int? ReviewJobTypeId,
    IReadOnlyList<int> LegacyJobTypeIds,
    int? OpinionJobTypeId,
    IReadOnlyList<string> Conflicts);
