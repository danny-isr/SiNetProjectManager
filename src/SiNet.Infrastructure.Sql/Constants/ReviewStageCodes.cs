namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>
/// Stable codes for <see cref="Models.WorkflowStageDefinition.Code"/> under
/// the <see cref="WorkflowCodes.Review"/> workflow (plan review — בדיקת תוכנית).
/// </summary>
public static class ReviewStageCodes
{
    public const string Intake                       = "REV.Intake";
    public const string AwaitingMunicipalityRequest  = "REV.AwaitingMunicipalityRequest";
    public const string ProjectSetup                 = "REV.ProjectSetup";
    public const string MaterialIntake               = "REV.MaterialIntake";
    public const string ProfessionalReview           = "REV.ProfessionalReview";
    public const string AwaitingManagerApproval      = "REV.AwaitingManagerApproval";
    public const string AwaitingPlannerCorrections   = "REV.AwaitingPlannerCorrections";
    public const string RecheckRound                 = "REV.RecheckRound";
    public const string PoliceSubmission             = "REV.PoliceSubmission";
    public const string AwaitingPoliceApproval       = "REV.AwaitingPoliceApproval";
    public const string AwaitingPoliceCorrections    = "REV.AwaitingPoliceCorrections";
    public const string PoliceApproved               = "REV.PoliceApproved";
    public const string Close                        = "REV.Close";
    public const string Completed                    = "REV.Completed";
}
