namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>
/// Stable codes for <see cref="Models.WorkflowStageDefinition.Code"/> under
/// the <see cref="WorkflowCodes.PlanningWorkflow"/> workflow.
/// </summary>
public static class PlanningStageCodes
{
    public const string Intake = "PLN.Intake";

    public const string QuoteProjectSetup = "PLN.Quote.ProjectSetup";
    public const string QuoteMaterialCheck = "PLN.Quote.MaterialCheck";
    public const string QuoteCalculation = "PLN.Quote.Calculation";
    public const string QuotePreparation = "PLN.Quote.Preparation";
    public const string QuoteInternalApproval = "PLN.Quote.InternalApproval";
    public const string QuoteSentFollowUp = "PLN.Quote.SentFollowUp";

    public const string WorkOrder = "PLN.WorkOrder";

    public const string ExecutionMaterialCheck = "PLN.Execution.MaterialCheck";
    public const string PlanningStart = "PLN.Planning.Start";

    public const string DesignDraft = "PLN.Design.Draft";
    public const string DesignPreliminary = "PLN.Design.Preliminary";
    public const string DesignDetailed = "PLN.Design.Detailed";

    public const string ApprovalSubmission = "PLN.Approval.Submission";
    public const string ApprovalComments = "PLN.Approval.Comments";
    public const string ApprovalAuthorityApproved = "PLN.Approval.AuthorityApproved";

    public const string DesignWorkPlans = "PLN.Design.WorkPlans";

    public const string BillingCheckMilestone = "PLN.Billing.CheckMilestone";

    public const string Close = "PLN.Close";
}
