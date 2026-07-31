namespace SiNet.Application.DevTools;

/// <summary>
/// Stable Codes that basic seed must leave in the database. Used for verify-only checks
/// (no writes). Keep in sync with Infrastructure seed constants / workflow seed.
/// </summary>
public static class SeedBaselineCatalog
{
    public static IReadOnlyList<string> RequiredWorkflowDefinitionCodes { get; } =
    [
        "MaterialIntake",
        "PlanningWorkflow",
        "Review",
        "Proposal",
        "Opinion",
    ];

    public static IReadOnlyList<string> RequiredUserGroupCodes { get; } =
    [
        "OfficeManagement",
        "SeniorManagement",
        "Planners",
        "ReviewIntake",
        "ProjectOpeners",
        "Reviewers",
        "ReviewManagers",
        "PoliceLiaison",
    ];

    public static IReadOnlyList<string> RequiredProjectFileCatalogCodes { get; } =
    [
        "QuoteEstimate",
        "QuoteDocument",
        "QuoteClientApproval",
        "QuoteClientRequest",
    ];

    /// <summary>JobType title required before catalog seed can attach Quote* rows.</summary>
    public const string RequiredJobTypeTitle = "חומר כללי";

    /// <summary>Folder title under the general JobType tree for quote catalog parents.</summary>
    public const string RequiredCorrespondenceFolderTitle = "תכתובת";
}
