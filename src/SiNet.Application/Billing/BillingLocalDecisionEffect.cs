namespace SiNet.Application.Billing;

/// <summary>Read-time effect of a stored local decision against current Replica facts.</summary>
public enum BillingLocalDecisionEffect
{
    None = 0,
    Active = 1,
    Expired = 2,
    Superseded = 3,
    Cleared = 4
}
