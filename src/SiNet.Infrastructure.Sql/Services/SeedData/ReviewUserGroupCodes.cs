namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Stable user-group codes used by the Review workflow.
/// Centralized so seed data and consuming services share a single source of truth.
/// </summary>
public static class ReviewUserGroupCodes
{
    public const string ReviewIntake    = "ReviewIntake";
    public const string ProjectOpeners  = "ProjectOpeners";
    public const string Reviewers       = "Reviewers";
    public const string ReviewManagers  = "ReviewManagers";
    public const string PoliceLiaison   = "PoliceLiaison";
}
