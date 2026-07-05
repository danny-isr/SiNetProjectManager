namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>Stable codes for <see cref="Models.WorkflowDefinition.Code"/>.</summary>
public static class WorkflowCodes
{
    /// <summary>The clean PLN.* planning workflow.</summary>
    public const string PlanningWorkflow = "PlanningWorkflow";

    /// <summary>Plan-review workflow (REV.*) — בדיקת תוכנית.</summary>
    public const string Review = "Review";

    /// <summary>Reusable material-intake subworkflow (MAT.*).</summary>
    public const string MaterialIntake = "MaterialIntake";

    /// <summary>
    /// Proposal / price-quote workflow (PRP.*). Project-independent — starts from an
    /// incoming email via <c>SuggestedActionType.CreatePriceQuote</c>; on client approval
    /// a real project is created and a continuation workflow is resolved through
    /// <see cref="Models.ProjectTypeWorkflowDefinition"/> /
    /// <see cref="Services.Workflow.ProjectWorkflowPolicyService"/>.
    /// </summary>
    public const string Proposal = "Proposal";

    /// <summary>
    /// Opinion / חוות דעת workflow (OPN.*). Started email-driven via
    /// <c>SuggestedActionType.CreateOpinionProject</c> which routes through
    /// <c>ActionExecutor.StartWorkflowFromActionAsync("Opinion", ...)</c>.
    /// Not mapped to any <see cref="Models.ProjectTypeWorkflowDefinition"/>.
    /// </summary>
    public const string Opinion = "Opinion";
}
