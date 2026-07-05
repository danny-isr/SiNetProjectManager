namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>
/// Stable codes for <see cref="Models.WorkflowStageDefinition.Code"/> under
/// the <see cref="WorkflowCodes.Opinion"/> workflow (OPN.*).
/// <para>
/// Opinion (חוות דעת) is an independent workflow started from an incoming
/// email via <c>SuggestedActionType.CreateOpinionProject</c>. Stages mirror
/// the canonical opinion lifecycle: material intake → analysis → draft →
/// internal review → send → close.
/// </para>
/// </summary>
public static class OpinionStageCodes
{
    public const string ReceiveMaterial        = "OPN.ReceiveMaterial";
    public const string AnalyzeDocuments       = "OPN.AnalyzeDocuments";
    public const string RequestMissingMaterial = "OPN.RequestMissingMaterial";
    public const string PrepareDraft           = "OPN.PrepareDraft";
    public const string InternalReview         = "OPN.InternalReview";
    public const string UpdateOpinion          = "OPN.UpdateOpinion";
    public const string SendOpinion            = "OPN.SendOpinion";

    /// <summary>Final stage — opinion delivered and process closed.</summary>
    public const string Close                  = "OPN.Close";
}
