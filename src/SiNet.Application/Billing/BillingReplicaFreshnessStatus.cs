namespace SiNet.Application.Billing;

/// <summary>Replica freshness for the billing dashboard. Age is vs now, never vs <c>AsOfDate</c>.</summary>
public enum BillingReplicaFreshnessStatus
{
    Healthy = 0,
    Warning = 1,
    Stale = 2
}
