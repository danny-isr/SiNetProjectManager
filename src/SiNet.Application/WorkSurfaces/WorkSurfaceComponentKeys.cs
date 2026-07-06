namespace SiNet.Application.WorkSurfaces;

/// <summary>
/// Stable component keys emitted by task navigation and honoured by work surfaces.
/// Values mirror <c>TaskComponentKeys</c> in Infrastructure.Sql — keep them aligned.
/// </summary>
public static class WorkSurfaceComponentKeys
{
    public const string ProjectCreationFromEmail = "Component.ProjectCreationFromEmail";
    public const string ReviewProjectSetupFromEmail = "Component.ReviewProjectSetupFromEmail";
    public const string EmailFiling = "Component.EmailFiling";
    public const string EmailComposeToPlanner = "Component.EmailComposeToPlanner";
    public const string InspectionReport = "Component.InspectionReport";
    public const string ManagerReviewApproval = "Component.ManagerReviewApproval";
    public const string MaterialChecklist = "Component.MaterialChecklist";
    public const string PoliceSubmission = "Component.PoliceSubmission";
    public const string BillingCheck = "Component.BillingCheck";
    public const string ProjectWork = "Component.ProjectWork";
    public const string GenericTask = "Component.GenericTask";

    /// <summary>True when the key should open the Email work surface.</summary>
    public static bool IsEmailSurface(string? componentKey) =>
        string.Equals(componentKey, EmailFiling, StringComparison.OrdinalIgnoreCase)
        || string.Equals(componentKey, ProjectCreationFromEmail, StringComparison.OrdinalIgnoreCase)
        || string.Equals(componentKey, ReviewProjectSetupFromEmail, StringComparison.OrdinalIgnoreCase)
        || string.Equals(componentKey, EmailComposeToPlanner, StringComparison.OrdinalIgnoreCase);
}
