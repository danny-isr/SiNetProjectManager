namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>
/// Stable codes for <see cref="Models.WorkflowStageDefinition.Code"/> under
/// the reusable <see cref="WorkflowCodes.MaterialIntake"/> subworkflow.
/// </summary>
public static class MaterialStageCodes
{
    public const string Receive             = "MAT.Receive";
    public const string File                = "MAT.File";
    public const string Check               = "MAT.Check";
    public const string AwaitingCompletion  = "MAT.AwaitingCompletion";
    public const string Complete            = "MAT.Complete";
}
