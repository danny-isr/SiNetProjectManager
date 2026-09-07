namespace SiNet.App.Wpf.Billing;

public enum BillingDashboardUiState
{
    Idle = 0,
    Loading = 1,
    Loaded = 2,
    FreshnessBlocked = 3,
    RecoverableError = 4,
    FatalError = 5
}
