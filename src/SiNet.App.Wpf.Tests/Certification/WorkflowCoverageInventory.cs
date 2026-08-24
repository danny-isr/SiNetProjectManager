using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Derives the certification coverage denominator from the target database at run time.
/// <para>
/// Deliberately not a hard-coded list. The Phase 1 audit could not even trust its own transition totals —
/// the source inventory reported sums that disagreed with its own tables — so the only trustworthy
/// denominator is the live graph. This also gives the property the operator asked for: adding a workflow
/// to the seed without a certification scenario breaks the run instead of silently reducing coverage.
/// </para>
/// </summary>
internal static class WorkflowCoverageInventory
{
    /// <summary>How a definition is accounted for. Every active definition needs one of these.</summary>
    internal enum Classification
    {
        /// <summary>A certification scenario drives this workflow end to end.</summary>
        Certified,

        /// <summary>Cannot be driven end to end because of a product or seed gap, with a written reason.</summary>
        Blocked,

        /// <summary>Out of scope for this tier, with a written reason.</summary>
        NotApplicable,
    }

    internal sealed record DefinitionCoverage(
        int Id,
        string Code,
        string Name,
        int StageCount,
        int TransitionCount,
        int SubWorkflowStageCount,
        string? EntryStageCode,
        IReadOnlyList<string> TerminalStageCodes);

    internal sealed record Inventory(
        IReadOnlyList<DefinitionCoverage> ActiveDefinitions,
        IReadOnlyList<string> Unclassified)
    {
        public int TotalStages => ActiveDefinitions.Sum(d => d.StageCount);

        public int TotalTransitions => ActiveDefinitions.Sum(d => d.TransitionCount);
    }

    /// <summary>
    /// Reads every active definition with its stage and transition counts, and reports any definition the
    /// caller has not classified. A non-empty <see cref="Inventory.Unclassified"/> must fail the run.
    /// </summary>
    public static async Task<Inventory> BuildAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IReadOnlyDictionary<string, (Classification Classification, string Reason)> classifications,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);
        ArgumentNullException.ThrowIfNull(classifications);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var definitions = await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .Select(d => new { d.Id, d.Code, d.Name })
            .OrderBy(d => d.Code)
            .ToListAsync(cancellationToken);

        var stages = await db.WorkflowStageDefinitions
            .AsNoTracking()
            .Select(s => new
            {
                s.Id,
                s.WorkflowDefinitionId,
                s.Code,
                s.IsInitial,
                s.IsFinal,
                s.NodeType,
                s.SortOrder,
            })
            .ToListAsync(cancellationToken);

        var transitionCounts = await db.WorkflowTransitionRules
            .AsNoTracking()
            .GroupBy(r => r.WorkflowDefinitionId)
            .Select(g => new { WorkflowDefinitionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var coverage = new List<DefinitionCoverage>();
        foreach (var definition in definitions)
        {
            var definitionStages = stages
                .Where(s => s.WorkflowDefinitionId == definition.Id)
                .ToList();

            coverage.Add(new DefinitionCoverage(
                definition.Id,
                definition.Code,
                definition.Name,
                definitionStages.Count,
                transitionCounts.FirstOrDefault(t => t.WorkflowDefinitionId == definition.Id)?.Count ?? 0,
                definitionStages.Count(s =>
                    string.Equals(s.NodeType, "SubWorkflow", StringComparison.OrdinalIgnoreCase)),
                definitionStages
                    .Where(s => s.IsInitial)
                    .OrderBy(s => s.SortOrder)
                    .Select(s => s.Code)
                    .FirstOrDefault(),
                definitionStages
                    .Where(s => s.IsFinal)
                    .OrderBy(s => s.SortOrder)
                    .Select(s => s.Code)
                    .ToList()));
        }

        var unclassified = coverage
            .Where(c => !classifications.ContainsKey(c.Code))
            .Select(c => c.Code)
            .ToList();

        return new Inventory(coverage, unclassified);
    }

    /// <summary>Renders the coverage table for the evidence report.</summary>
    public static string Describe(Inventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        return string.Join(
            " | ",
            inventory.ActiveDefinitions.Select(d =>
                $"{d.Code}: {d.StageCount} stages, {d.TransitionCount} transitions, "
                + $"{d.SubWorkflowStageCount} sub-workflow stages, entry={d.EntryStageCode ?? "<none>"}, "
                + $"terminal=[{string.Join(",", d.TerminalStageCodes)}]"));
    }
}
