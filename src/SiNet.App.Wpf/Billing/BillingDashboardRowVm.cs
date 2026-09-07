using SiNet.Application.Billing;

namespace SiNet.App.Wpf.Billing;

/// <summary>Grid/detail projection of a service candidate row. Does not recompute <see cref="BillingCandidateState"/>.</summary>
public sealed class BillingDashboardRowVm
{
    public BillingDashboardRowVm(BillingCandidateRow source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public BillingCandidateRow Source { get; }

    public int ProjectId => Source.ProjectId;
    public BillingCandidateState CandidateState => Source.CandidateState;
    public string StateDisplay => BillingCandidateStateDisplay.ToHebrew(Source.CandidateState);
    public string? ProjectNumber => Source.ProjectNumber;
    public string? ProjectName => Source.ProjectName;
    public string? CustomerName => Source.CustomerName;
    public string? ProjectStatus => Source.ProjectStatus;
    public string LastWorkDateText => BillingDashboardFormatters.Date(Source.LastWorkDate);
    public string Hours30Text => BillingDashboardFormatters.Hours(Source.Hours30);
    public string Hours60Text => BillingDashboardFormatters.Hours(Source.Hours60);
    public string Hours90Text => BillingDashboardFormatters.Hours(Source.Hours90);
    public string HoursSinceLastBillText => BillingDashboardFormatters.Hours(Source.HoursSinceLastBill);
    public int WorkDays30 => Source.WorkDays30;
    public string LastBillDateText => BillingDashboardFormatters.Date(Source.LastBillDate);
    public string DaysSinceLastBillText => Source.DaysSinceLastBill?.ToString() ?? BillingDashboardFormatters.EmDash;
    public string? LatestBillStatus => Source.LatestBillStatus;
    public string CurrentFeeSumText => BillingDashboardFormatters.Money(Source.CurrentFeeSum);
    public string SnapshotBalanceText => BillingDashboardFormatters.Money(Source.SnapshotBalance);
    public string SnapshotOpenBillSumText => BillingDashboardFormatters.Money(Source.SnapshotOpenBillSum);
    public string SnapshotApprovedBillSumText => BillingDashboardFormatters.Money(Source.SnapshotApprovedBillSum);
    public string SnapshotBilledPercentText => Source.SnapshotBilledPercent is decimal percent
        ? percent.ToString("0.##") + "%"
        : BillingDashboardFormatters.EmDash;
    public string SnapshotDateText => BillingDashboardFormatters.SnapshotDateText(Source.SnapshotDate);
    public string SnapshotBalanceCaption => BillingDashboardFormatters.SnapshotBalanceCaption(Source.SnapshotDate);
    public string FeeClassification => BillingDashboardFormatters.FeeMix(Source.SnapshotFeeTypes?.Mix);
    public string FeeTypesTooltip => BillingDashboardFormatters.FeeTooltip(Source.SnapshotFeeTypes);
    public string CandidateReason => Source.CandidateReason;
    public string WorkDays30Text => Source.WorkDays30.ToString();
    public string RecentHoursSummary =>
        $"30: {Hours30Text} · 60: {Hours60Text} · 90: {Hours90Text}";
    public string BillsInFlightSummary =>
        $"ביצירה: {Source.BillsInCreation} · הוגשו: {Source.BillsSubmitted}";
    public string LatestBillSummary =>
        Source.LatestBillId is int id
            ? $"{Source.LatestBillNumber ?? id.ToString()} · {Source.LatestBillStatus ?? BillingDashboardFormatters.EmDash} · {BillingDashboardFormatters.Money(Source.LatestBillSum)}"
            : BillingDashboardFormatters.EmDash;
}
