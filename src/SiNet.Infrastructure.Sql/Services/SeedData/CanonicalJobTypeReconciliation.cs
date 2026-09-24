using Microsoft.EntityFrameworkCore;
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
        if (canonical is not null)
        {
            foreach (var row in legacy)
            {
                conflicts.AddRange(await CanonicalJobTypeSeed
                    .DescribeMergeConflictsAsync(db, row.Id, canonical.Id, ct)
                    .ConfigureAwait(false));
            }
        }

        return new CanonicalJobTypeReconciliationReport(
            canonical?.Id,
            legacy.Select(j => j.Id).ToArray(),
            opinion?.Id,
            conflicts);
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

        await CanonicalJobTypeSeed.NormalizeJobTypesAsync(db, ct).ConfigureAwait(false);
        await CanonicalJobTypeSeed.EnsureWorkflowMappingsAsync(db, ct).ConfigureAwait(false);
        await CanonicalJobTypeSeed.EnsureStageProfilesAsync(db, ct).ConfigureAwait(false);
    }
}

public sealed record CanonicalJobTypeReconciliationReport(
    int? ReviewJobTypeId,
    IReadOnlyList<int> LegacyJobTypeIds,
    int? OpinionJobTypeId,
    IReadOnlyList<string> Conflicts);
