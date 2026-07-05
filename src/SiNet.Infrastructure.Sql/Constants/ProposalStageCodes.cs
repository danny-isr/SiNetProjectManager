namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>
/// Stable codes for <see cref="Models.WorkflowStageDefinition.Code"/> under
/// the <see cref="WorkflowCodes.Proposal"/> workflow (PRP.*).
/// <para>
/// Semantically these mirror the first seven quote stages of PlanningWorkflow
/// (<see cref="PlanningStageCodes"/>) — Proposal owns the price-quote lifecycle
/// as an independent workflow. The PLN.* equivalents are kept temporarily
/// in PlanningWorkflow for legacy compatibility (see <c>PlanningWorkflowSeedData</c>).
/// </para>
/// </summary>
public static class ProposalStageCodes
{
    public const string Intake             = "PRP.Intake";
    public const string ProjectSetup       = "PRP.ProjectSetup";

    /// <summary>
    /// Dedicated filing stage for the quote material that arrived with the
    /// originating email. Owns the <c>FileQuoteMaterial</c> task; closure is
    /// driven by the existing <c>ReviewMaterialFiled</c> event raised by
    /// <c>MoveToProjectProcessActionHandler</c>.
    /// </summary>
    public const string FileMaterial       = "PRP.FileMaterial";

    public const string MaterialCheck      = "PRP.MaterialCheck";
    public const string Calculation        = "PRP.Calculation";
    public const string Preparation        = "PRP.Preparation";
    public const string InternalApproval   = "PRP.InternalApproval";
    public const string SentFollowUp       = "PRP.SentFollowUp";

    /// <summary>Final stage when the client approves the quote.</summary>
    public const string Approved           = "PRP.Approved";

    /// <summary>Final stage when the client rejects the quote.</summary>
    public const string Rejected           = "PRP.Rejected";
}
