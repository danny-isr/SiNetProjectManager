namespace SiNet.Application.Billing;

public enum BillingPreparationStatus
{
    WaitingForSnapshot = 1,
    WaitingForSelection = 2,
    ReadyForApproval = 3,
    TaskOpen = 4,
    AwaitingMasterPlanConfirmation = 5,
    Completed = 6,
    Cancelled = 7
}

public static class BillingPreparationStatusExtensions
{
    public static bool IsActiveCycle(this BillingPreparationStatus status) =>
        status is not BillingPreparationStatus.Completed and not BillingPreparationStatus.Cancelled;

    public static string ToHebrew(this BillingPreparationStatus status) => status switch
    {
        BillingPreparationStatus.WaitingForSnapshot => "ממתין לנתוני MasterPlan",
        BillingPreparationStatus.WaitingForSelection => "ממתין לבחירת רכיבים",
        BillingPreparationStatus.ReadyForApproval => "מוכן לאישור",
        BillingPreparationStatus.TaskOpen => "משימה פתוחה",
        BillingPreparationStatus.AwaitingMasterPlanConfirmation => "ממתין לעדכון MasterPlan",
        BillingPreparationStatus.Completed => "הושלם",
        BillingPreparationStatus.Cancelled => "בוטל",
        _ => status.ToString()
    };
}

/// <summary>MasterPlan <c>Bills.StatusID</c> values that count as submitted for observed stage progress.</summary>
public static class BillingAcceptedBillStatusIds
{
    public const int InCreation = 1;
    public const int Submitted = 2;
    public const int Approved = 3;
    public const int Closed = 4;

    public static bool CountsAsSubmitted(int statusId) =>
        statusId is Submitted or Approved or Closed;
}

public enum BillingConfirmationMode
{
    None = 0,
    NewerSnapshot = 1,
    Manual = 2
}
