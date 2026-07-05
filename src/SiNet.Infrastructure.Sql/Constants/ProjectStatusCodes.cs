namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>Stable codes for <see cref="Models.ProjectStatus.Code"/>.</summary>
public static class ProjectStatusCodes
{
    public const string LeadReceived = "LeadReceived";
    public const string QuotePreparation = "QuotePreparation";
    public const string WaitingForQuoteApproval = "WaitingForQuoteApproval";
    public const string WaitingForWorkOrder = "WaitingForWorkOrder";
    public const string Active = "Active";
    public const string WaitingForClient = "WaitingForClient";
    public const string WaitingForAuthority = "WaitingForAuthority";
    public const string WaitingForMaterial = "WaitingForMaterial";
    public const string BillingPending = "BillingPending";
    public const string Closed = "Closed";
    public const string ClosedLost = "ClosedLost";
    public const string Cancelled = "Cancelled";
}
