using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Canonical JobType taxonomy for Review and Opinion.
/// Idempotent: rename-in-place when possible, merge only when both titles already exist.
/// </summary>
internal static class CanonicalJobTypeSeed
{
    public const string ReviewJobTypeTitle = "בדיקה";
    public const string OpinionJobTypeTitle = "חוות דעת";

    /// <summary>
    /// Legacy combined title. The spaced form is the product name; the underscore
    /// form is the title already stored on existing rows.
    /// </summary>
    public static readonly string[] LegacyReviewJobTypeTitles =
    [
        "בדיקה חוות דעת",
        "בדיקה_חוות_דעת",
    ];

    public static bool IsReviewOrOpinionTitle(string? title) =>
        string.Equals(title, ReviewJobTypeTitle, StringComparison.Ordinal)
        || string.Equals(title, OpinionJobTypeTitle, StringComparison.Ordinal);

    public static async Task NormalizeJobTypesAsync(SiNetSQLDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        await EnsureReviewJobTypeAsync(db, ct).ConfigureAwait(false);
        await EnsureJobTypeByExactTitleAsync(db, OpinionJobTypeTitle, ct).ConfigureAwait(false);
    }

    public static async Task EnsureWorkflowMappingsAsync(SiNetSQLDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        await EnsureMappingAsync(db, ReviewJobTypeTitle, WorkflowCodes.Review, ct).ConfigureAwait(false);
        await EnsureMappingAsync(db, OpinionJobTypeTitle, WorkflowCodes.Opinion, ct).ConfigureAwait(false);
    }

    public static async Task EnsureStageProfilesAsync(SiNetSQLDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        await EnsureProfileAsync(
            db,
            ReviewJobTypeTitle,
            WorkflowCodes.Review,
            ReviewWorkflowSeedData.Stages.Select(s => (
                s.Code,
                s.SortOrder,
                IsRequired: !ReviewWorkflowSeedData.OptionalStageCodes.Contains(s.Code),
                CanRepeat: false)).ToArray(),
            ct).ConfigureAwait(false);

        await EnsureProfileAsync(
            db,
            OpinionJobTypeTitle,
            WorkflowCodes.Opinion,
            OpinionWorkflowSeedData.Stages.Select(s => (
                s.Code,
                s.SortOrder,
                IsRequired: true,
                CanRepeat: false)).ToArray(),
            ct).ConfigureAwait(false);
    }

    private static async Task EnsureReviewJobTypeAsync(SiNetSQLDbContext db, CancellationToken ct)
    {
        var jobTypes = await db.JobTypes.Where(j => j.Title != null).ToListAsync(ct).ConfigureAwait(false);
        var canonical = jobTypes.FirstOrDefault(j =>
            string.Equals(j.Title, ReviewJobTypeTitle, StringComparison.Ordinal));
        var legacy = jobTypes
            .Where(j => LegacyReviewJobTypeTitles.Contains(j.Title!, StringComparer.Ordinal))
            .ToList();

        if (canonical is null && legacy.Count == 0)
        {
            db.JobTypes.Add(new JobType { Title = ReviewJobTypeTitle });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            DevToolsLog.Info("[WorkflowSeed] Created JobType 'בדיקה'.");
            return;
        }

        var plannedConflicts = await DescribePlannedReviewConflictsAsync(
            db, canonical?.Id, legacy.Select(j => j.Id).ToArray(), ct).ConfigureAwait(false);
        if (plannedConflicts.Count > 0)
        {
            throw new InvalidOperationException(
                "Canonical JobType reconciliation stopped. No rows were changed. "
                + string.Join(" ", plannedConflicts));
        }

        if (canonical is null)
        {
            canonical = legacy[0];
            legacy.RemoveAt(0);
            canonical.Title = ReviewJobTypeTitle;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            DevToolsLog.Info($"[WorkflowSeed] Renamed JobType #{canonical.Id} to 'בדיקה'.");
        }

        foreach (var row in legacy)
            await MergeJobTypeIntoAsync(db, row, canonical, ct).ConfigureAwait(false);
    }

    private static async Task EnsureJobTypeByExactTitleAsync(
        SiNetSQLDbContext db, string title, CancellationToken ct)
    {
        var exists = await db.JobTypes.AnyAsync(
            j => j.Title == title, ct).ConfigureAwait(false);
        if (exists)
            return;

        db.JobTypes.Add(new JobType { Title = title });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        DevToolsLog.Info($"[WorkflowSeed] Created JobType '{title}'.");
    }

    private static async Task EnsureMappingAsync(
        SiNetSQLDbContext db, string jobTypeTitle, string workflowCode, CancellationToken ct)
    {
        var jobTypeId = await db.JobTypes.AsNoTracking()
            .Where(j => j.Title == jobTypeTitle)
            .Select(j => (int?)j.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var workflowId = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Code == workflowCode)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var planningId = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Code == WorkflowCodes.PlanningWorkflow)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (jobTypeId is null)
            throw new InvalidOperationException($"Blocked: JobType '{jobTypeTitle}' is missing. Reconciliation did not finish mappings.");
        if (workflowId is null)
            throw new InvalidOperationException($"Blocked: workflow definition '{workflowCode}' is missing. Reconciliation did not finish mappings.");

        var mappings = await db.ProjectTypeWorkflowDefinitions
            .Where(m => m.ProjectTypeId == jobTypeId.Value)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var canonical = mappings.FirstOrDefault(m => m.WorkflowDefinitionId == workflowId.Value);
        if (canonical is null)
        {
            canonical = new ProjectTypeWorkflowDefinition
            {
                ProjectTypeId = jobTypeId.Value,
                WorkflowDefinitionId = workflowId.Value,
            };
            db.ProjectTypeWorkflowDefinitions.Add(canonical);
        }

        canonical.IsEnabled = true;
        canonical.IsDefault = true;
        canonical.SortOrder = 1;

        if (planningId is int planning && planning != workflowId.Value)
        {
            var planningMapping = mappings.FirstOrDefault(m => m.WorkflowDefinitionId == planning);
            if (planningMapping is not null)
            {
                planningMapping.IsEnabled = false;
                planningMapping.IsDefault = false;
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task EnsureProfileAsync(
        SiNetSQLDbContext db,
        string jobTypeTitle,
        string workflowCode,
        IReadOnlyList<(string Code, int SortOrder, bool IsRequired, bool CanRepeat)> stages,
        CancellationToken ct)
    {
        var jobTypeId = await db.JobTypes.AsNoTracking()
            .Where(j => j.Title == jobTypeTitle)
            .Select(j => (int?)j.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        var workflowId = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Code == workflowCode)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (jobTypeId is null)
            throw new InvalidOperationException($"Blocked: JobType '{jobTypeTitle}' is missing. Reconciliation did not finish stage profiles.");
        if (workflowId is null)
            throw new InvalidOperationException($"Blocked: workflow definition '{workflowCode}' is missing. Reconciliation did not finish stage profiles.");

        var stageIds = await db.WorkflowStageDefinitions.AsNoTracking()
            .Where(s => s.WorkflowDefinitionId == workflowId.Value)
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.OrdinalIgnoreCase, ct)
            .ConfigureAwait(false);
        var existing = await db.ProjectTypeWorkflowStages
            .Where(s => s.ProjectTypeId == jobTypeId.Value)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var stage in stages)
        {
            if (!stageIds.TryGetValue(stage.Code, out var stageId))
                throw new InvalidOperationException($"Blocked: stage '{stage.Code}' is missing from workflow '{workflowCode}'.");

            var row = existing.FirstOrDefault(s => s.WorkflowStageDefinitionId == stageId);
            if (row is null)
            {
                row = new ProjectTypeWorkflowStage
                {
                    ProjectTypeId = jobTypeId.Value,
                    WorkflowStageDefinitionId = stageId,
                };
                db.ProjectTypeWorkflowStages.Add(row);
                existing.Add(row);
            }

            row.IsActive = true;
            row.IsRequired = stage.IsRequired;
            row.CanRepeat = stage.CanRepeat;
            row.SortOrder = stage.SortOrder;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task MergeJobTypeIntoAsync(
        SiNetSQLDbContext db, JobType source, JobType target, CancellationToken ct)
    {
        var conflicts = await DescribeMergeConflictsAsync(db, source.Id, target.Id, ct).ConfigureAwait(false);
        if (conflicts.Count > 0)
        {
            throw new InvalidOperationException(
                "Canonical JobType reconciliation stopped. No rows were changed. "
                + string.Join(" ", conflicts));
        }

        var ownsTransaction = db.Database.CurrentTransaction is null && db.Database.IsRelational();
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;
        try
        {
            await MoveProjectLinksAsync(db, source.Id, target.Id, ct).ConfigureAwait(false);
            await MoveSurrogatePairsAsync(
                db.ProjectTypeWorkflowDefinitions,
                db.ProjectTypeWorkflowDefinitions.Where(m => m.ProjectTypeId == source.Id),
                db.ProjectTypeWorkflowDefinitions.Where(m => m.ProjectTypeId == target.Id),
                (m, id) => m.ProjectTypeId = id,
                m => m.WorkflowDefinitionId,
                target.Id,
                ct).ConfigureAwait(false);
            await MoveSurrogatePairsAsync(
                db.ProjectTypeWorkflowStages,
                db.ProjectTypeWorkflowStages.Where(m => m.ProjectTypeId == source.Id),
                db.ProjectTypeWorkflowStages.Where(m => m.ProjectTypeId == target.Id),
                (m, id) => m.ProjectTypeId = id,
                m => m.WorkflowStageDefinitionId,
                target.Id,
                ct).ConfigureAwait(false);
            await MoveSurrogatePairsAsync(
                db.ProjectTypeDisciplines,
                db.ProjectTypeDisciplines.Where(m => m.ProjectTypeId == source.Id),
                db.ProjectTypeDisciplines.Where(m => m.ProjectTypeId == target.Id),
                (m, id) => m.ProjectTypeId = id,
                m => m.DisciplineTaskTypeId,
                target.Id,
                ct).ConfigureAwait(false);
            await MoveCompositePairsAsync(
                db.ProjectTypeStatuses,
                db.ProjectTypeStatuses.Where(m => m.ProjectTypeId == source.Id),
                db.ProjectTypeStatuses.Where(m => m.ProjectTypeId == target.Id),
                m => m.StatusId,
                statusId => new ProjectTypeStatus { ProjectTypeId = target.Id, StatusId = statusId },
                ct).ConfigureAwait(false);
            await MoveCompositePairsAsync(
                db.ProjectTypeTaskTypes,
                db.ProjectTypeTaskTypes.Where(m => m.ProjectTypeId == source.Id),
                db.ProjectTypeTaskTypes.Where(m => m.ProjectTypeId == target.Id),
                m => m.TaskTypeId,
                taskTypeId => new ProjectTypeTaskType { ProjectTypeId = target.Id, TaskTypeId = taskTypeId },
                ct).ConfigureAwait(false);
            await MoveBidsAsync(db, source.Id, target.Id, ct).ConfigureAwait(false);
            await MoveSimpleAsync(
                db.PaymentsSteps.Where(p => p.JobTypeId == source.Id),
                row => row.JobTypeId = target.Id,
                ct).ConfigureAwait(false);
            await MoveSimpleAsync(
                db.ProjectFiles.Where(p => p.TypeProjId == source.Id),
                row => row.TypeProjId = target.Id,
                ct).ConfigureAwait(false);
            await MoveWorkflowInstancesAsync(db, source.Id, target.Id, ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            var stillReferenced = await HasJobTypeReferencesAsync(db, source.Id, ct).ConfigureAwait(false);
            if (stillReferenced)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                DevToolsLog.Warn($"[WorkflowSeed] JobType #{source.Id} '{source.Title}' still has references; left in place.");
                return;
            }

            db.JobTypes.Remove(source);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (transaction is not null)
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            DevToolsLog.Info($"[WorkflowSeed] Removed legacy JobType #{source.Id} after merging into #{target.Id}.");
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<IReadOnlyList<string>> DescribePlannedReviewConflictsAsync(
        SiNetSQLDbContext db, int? canonicalId, IReadOnlyList<int> legacyIds, CancellationToken ct)
    {
        var conflicts = new List<string>();
        if (canonicalId is int keeper)
        {
            foreach (var legacyId in legacyIds)
            {
                conflicts.AddRange(await DescribeMergeConflictsAsync(db, legacyId, keeper, ct).ConfigureAwait(false));
            }

            return conflicts;
        }

        if (legacyIds.Count < 2)
            return conflicts;

        var futureKeeper = legacyIds[0];
        foreach (var legacyId in legacyIds.Skip(1))
        {
            conflicts.AddRange(await DescribeMergeConflictsAsync(db, legacyId, futureKeeper, ct).ConfigureAwait(false));
        }

        return conflicts;
    }

    internal static async Task<IReadOnlyList<string>> DescribeMissingWorkflowPrerequisitesAsync(
        SiNetSQLDbContext db, CancellationToken ct)
    {
        var blocks = new List<string>();
        foreach (var (code, stages) in new[]
        {
            (WorkflowCodes.Review, ReviewWorkflowSeedData.Stages),
            (WorkflowCodes.Opinion, OpinionWorkflowSeedData.Stages),
        })
        {
            var definition = await db.WorkflowDefinitions.AsNoTracking()
                .Where(d => d.Code == code)
                .Select(d => new { d.Id, d.Code })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (definition is null)
            {
                blocks.Add($"Blocked: workflow definition '{code}' is missing. Reconciliation did not write.");
                continue;
            }

            var present = await db.WorkflowStageDefinitions.AsNoTracking()
                .Where(s => s.WorkflowDefinitionId == definition.Id)
                .Select(s => s.Code)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var presentSet = present.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stage in stages)
            {
                if (!presentSet.Contains(stage.Code))
                    blocks.Add($"Blocked: stage '{stage.Code}' is missing from workflow '{code}'. Reconciliation did not write.");
            }
        }

        return blocks;
    }

    internal static async Task<IReadOnlyList<string>> DescribeMergeConflictsAsync(
        SiNetSQLDbContext db, int sourceId, int targetId, CancellationToken ct)
    {
        var conflicts = new List<string>();
        var targetProjects = (await db.Bids.AsNoTracking()
            .Where(b => b.JobTypeId == targetId)
            .Select(b => b.ProjectsId)
            .ToListAsync(ct)
            .ConfigureAwait(false)).ToHashSet();
        var sourceBids = await db.Bids.AsNoTracking()
            .Where(b => b.JobTypeId == sourceId && targetProjects.Contains(b.ProjectsId))
            .Select(b => new { b.Id, b.ProjectsId, b.BidValue })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var bid in sourceBids)
        {
            conflicts.Add(
                $"Conflict: Bid #{bid.Id} project {bid.ProjectsId} amount {bid.BidValue} exists on both JobTypes. Neither bid is deleted.");
        }

        var sourceLive = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.JobTypeId == sourceId
                && (i.Status == WorkflowStatus.Active || i.Status == WorkflowStatus.Paused))
            .Select(i => new { i.Id, i.ProjectId, i.WorkflowDefinitionId })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var row in sourceLive)
        {
            var clash = await db.WorkflowInstances.AsNoTracking().AnyAsync(
                i => i.Id != row.Id
                    && i.ProjectId == row.ProjectId
                    && i.WorkflowDefinitionId == row.WorkflowDefinitionId
                    && i.JobTypeId == targetId
                    && (i.Status == WorkflowStatus.Active || i.Status == WorkflowStatus.Paused),
                ct).ConfigureAwait(false);
            if (clash)
            {
                conflicts.Add(
                    $"Conflict: active workflow #{row.Id} on project {row.ProjectId} definition {row.WorkflowDefinitionId} already exists on the canonical JobType.");
            }
        }

        return conflicts;
    }

    private static async Task MoveProjectLinksAsync(
        SiNetSQLDbContext db, int fromId, int toId, CancellationToken ct)
    {
        var rows = await db.TypeOfProjectInProjects
            .Where(t => t.ProjectTypeId == fromId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var occupied = (await db.TypeOfProjectInProjects
            .Where(t => t.ProjectTypeId == toId && t.ProjectId != null)
            .Select(t => t.ProjectId!.Value)
            .ToListAsync(ct)
            .ConfigureAwait(false)).ToHashSet();
        foreach (var row in rows)
        {
            if (row.ProjectId is int projectId && !occupied.Add(projectId))
            {
                db.TypeOfProjectInProjects.Remove(row);
                continue;
            }

            row.ProjectTypeId = toId;
        }
    }

    private static async Task MoveSurrogatePairsAsync<T>(
        DbSet<T> set,
        IQueryable<T> sourceRows,
        IQueryable<T> targetRows,
        Action<T, int> setProjectTypeId,
        Func<T, int> otherKey,
        int toId,
        CancellationToken ct)
        where T : class
    {
        var rows = await sourceRows.ToListAsync(ct).ConfigureAwait(false);
        var occupied = (await targetRows.ToListAsync(ct).ConfigureAwait(false))
            .Select(otherKey)
            .ToHashSet();
        foreach (var row in rows)
        {
            if (!occupied.Add(otherKey(row)))
            {
                set.Remove(row);
                continue;
            }

            setProjectTypeId(row, toId);
        }
    }

    private static async Task MoveCompositePairsAsync<T>(
        DbSet<T> set,
        IQueryable<T> sourceRows,
        IQueryable<T> targetRows,
        Func<T, int> otherKey,
        Func<int, T> create,
        CancellationToken ct)
        where T : class
    {
        var rows = await sourceRows.ToListAsync(ct).ConfigureAwait(false);
        var occupied = (await targetRows.ToListAsync(ct).ConfigureAwait(false))
            .Select(otherKey)
            .ToHashSet();
        foreach (var row in rows)
        {
            var key = otherKey(row);
            if (occupied.Add(key))
                set.Add(create(key));

            set.Remove(row);
        }
    }

    private static async Task MoveBidsAsync(SiNetSQLDbContext db, int fromId, int toId, CancellationToken ct)
    {
        var rows = await db.Bids.Where(b => b.JobTypeId == fromId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in rows)
            row.JobTypeId = toId;
    }

    private static async Task<bool> HasJobTypeReferencesAsync(
        SiNetSQLDbContext db, int jobTypeId, CancellationToken ct) =>
        await db.TypeOfProjectInProjects.AnyAsync(t => t.ProjectTypeId == jobTypeId, ct).ConfigureAwait(false)
        || await db.ProjectTypeWorkflowDefinitions.AnyAsync(t => t.ProjectTypeId == jobTypeId, ct).ConfigureAwait(false)
        || await db.ProjectTypeWorkflowStages.AnyAsync(t => t.ProjectTypeId == jobTypeId, ct).ConfigureAwait(false)
        || await db.ProjectTypeDisciplines.AnyAsync(t => t.ProjectTypeId == jobTypeId, ct).ConfigureAwait(false)
        || await db.ProjectTypeStatuses.AnyAsync(t => t.ProjectTypeId == jobTypeId, ct).ConfigureAwait(false)
        || await db.ProjectTypeTaskTypes.AnyAsync(t => t.ProjectTypeId == jobTypeId, ct).ConfigureAwait(false)
        || await db.Bids.AnyAsync(t => t.JobTypeId == jobTypeId, ct).ConfigureAwait(false)
        || await db.PaymentsSteps.AnyAsync(t => t.JobTypeId == jobTypeId, ct).ConfigureAwait(false)
        || await db.ProjectFiles.AnyAsync(t => t.TypeProjId == jobTypeId, ct).ConfigureAwait(false)
        || await db.WorkflowInstances.AnyAsync(t => t.JobTypeId == jobTypeId, ct).ConfigureAwait(false);

    private static async Task MoveSimpleAsync<T>(
        IQueryable<T> query, Action<T> assign, CancellationToken ct)
        where T : class
    {
        var rows = await query.ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in rows)
            assign(row);
    }

    private static async Task MoveWorkflowInstancesAsync(
        SiNetSQLDbContext db, int fromId, int toId, CancellationToken ct)
    {
        var rows = await db.WorkflowInstances.Where(i => i.JobTypeId == fromId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in rows)
            row.JobTypeId = toId;
    }
}
