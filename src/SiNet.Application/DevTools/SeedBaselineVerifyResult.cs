namespace SiNet.Application.DevTools;

/// <summary>Read-only outcome of comparing <see cref="SeedBaselineCatalog"/> to the database.</summary>
public sealed record SeedBaselineVerifyResult(
    IReadOnlyList<string> MissingWorkflowDefinitionCodes,
    IReadOnlyList<string> MissingUserGroupCodes,
    IReadOnlyList<string> MissingProjectFileCatalogCodes,
    bool JobTypePresent,
    bool CorrespondenceFolderPresent,
    IReadOnlyList<string> JobTypesMissingWorkflowMapping)
{
    public bool HasRequiredGaps =>
        MissingWorkflowDefinitionCodes.Count > 0
        || MissingUserGroupCodes.Count > 0
        || MissingProjectFileCatalogCodes.Count > 0
        || JobTypesMissingWorkflowMapping.Count > 0;

    public bool HasPrerequisiteWarnings => !JobTypePresent || !CorrespondenceFolderPresent;

    public bool IsComplete => !HasRequiredGaps && !HasPrerequisiteWarnings;

    /// <summary>Pure evaluate from observed Code sets (unit-testable without SQL).</summary>
    public static SeedBaselineVerifyResult Evaluate(
        IReadOnlyCollection<string> presentWorkflowCodes,
        IReadOnlyCollection<string> presentUserGroupCodes,
        IReadOnlyCollection<string> presentCatalogCodes,
        bool jobTypePresent,
        bool correspondenceFolderPresent,
        IReadOnlyCollection<string>? jobTypesMissingWorkflowMapping = null)
    {
        ArgumentNullException.ThrowIfNull(presentWorkflowCodes);
        ArgumentNullException.ThrowIfNull(presentUserGroupCodes);
        ArgumentNullException.ThrowIfNull(presentCatalogCodes);

        IReadOnlyList<string> missingMappings = jobTypesMissingWorkflowMapping is null
            ? []
            : jobTypesMissingWorkflowMapping
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();

        return new SeedBaselineVerifyResult(
            Missing(SeedBaselineCatalog.RequiredWorkflowDefinitionCodes, presentWorkflowCodes),
            Missing(SeedBaselineCatalog.RequiredUserGroupCodes, presentUserGroupCodes),
            Missing(SeedBaselineCatalog.RequiredProjectFileCatalogCodes, presentCatalogCodes),
            jobTypePresent,
            correspondenceFolderPresent,
            missingMappings);
    }

    public string FormatSummaryHe()
    {
        if (IsComplete)
            return "Seed בסיסי שלם (Codes נדרשים קיימים).";

        var parts = new List<string>();
        if (MissingWorkflowDefinitionCodes.Count > 0)
            parts.Add("Workflow: " + string.Join(", ", MissingWorkflowDefinitionCodes));
        if (MissingUserGroupCodes.Count > 0)
            parts.Add("קבוצות: " + string.Join(", ", MissingUserGroupCodes));
        if (MissingProjectFileCatalogCodes.Count > 0)
            parts.Add("Catalog: " + string.Join(", ", MissingProjectFileCatalogCodes));
        if (JobTypesMissingWorkflowMapping.Count > 0)
        {
            var preview = JobTypesMissingWorkflowMapping.Count <= 5
                ? string.Join(", ", JobTypesMissingWorkflowMapping)
                : string.Join(", ", JobTypesMissingWorkflowMapping.Take(5))
                  + $" (+{JobTypesMissingWorkflowMapping.Count - 5})";
            parts.Add("סוגי פרויקט בלי מיפוי תהליך: " + preview);
        }

        if (!JobTypePresent)
            parts.Add($"חסר JobType «{SeedBaselineCatalog.RequiredJobTypeTitle}»");
        if (!CorrespondenceFolderPresent)
            parts.Add($"חסרה תיקייה «{SeedBaselineCatalog.RequiredCorrespondenceFolderTitle}»");

        return string.Join(" · ", parts);
    }

    private static IReadOnlyList<string> Missing(
        IReadOnlyList<string> required,
        IReadOnlyCollection<string> present)
    {
        var set = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
        return required.Where(c => !set.Contains(c)).ToList();
    }
}
