namespace SiNet.Application.Billing;

/// <summary>MasterPlan bill lifecycle statuses. SiNet must not move bills between these states.</summary>
public static class MasterPlanBillStatusIds
{
    public const int InCreation = 1;
    public const int Submitted = 2;
    public const int Approved = 3;
    public const int Closed = 4;

    /// <summary>
    /// Statuses that close the previous work period for <c>HoursSinceLastBill</c>.
    /// A bill in ביצירה does not reset that period.
    /// </summary>
    public static bool ResetsWorkPeriod(int? statusId) =>
        statusId is Submitted or Approved or Closed;
}
