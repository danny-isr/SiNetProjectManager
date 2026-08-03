namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>
/// Stable codes for <see cref="Models.WorkflowStageDefinition.Code"/> under
/// <see cref="WorkflowCodes.Outsourcing"/>.
/// </summary>
public static class OutsourcingStageCodes
{
    public const string ReceiveOffer = "OUT.ReceiveOffer";
    public const string ApproveOffer = "OUT.ApproveOffer";
    public const string MonitorPayments = "OUT.MonitorPayments";
    public const string Complete = "OUT.Complete";
}
