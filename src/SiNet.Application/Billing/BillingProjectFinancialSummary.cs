using System.Globalization;

namespace SiNet.Application.Billing;

/// <summary>
/// Whole-project financial card for a SiNet preparation that is not yet a MasterPlan submitted bill.
/// Total fee is Replica <c>MP_Projects.FeeSum</c>. Balance is monthly <c>ProjectsExtraData.Balance</c>.
/// </summary>
public sealed record BillingProjectFinancialSummary(
    string TotalFeeText,
    string BalanceBeforeText,
    string AlreadyBilledText,
    string CurrentBillText,
    string BalanceAfterText,
    decimal? TotalFee,
    decimal? BalanceBefore,
    decimal? AlreadyBilled,
    decimal? CurrentBill,
    decimal? BalanceAfter,
    decimal ObservedBarShare,
    decimal AdditionBarShare,
    decimal RemainingBarShare,
    bool ShowBar,
    string? BarUnavailableReason,
    string SourceLine,
    string? ExceedsBalanceWarning,
    string? HourlyNote,
    string? InCreationNote)
{
    public bool ShowExceedsWarning => !string.IsNullOrWhiteSpace(ExceedsBalanceWarning);
    public bool ShowHourlyNote => !string.IsNullOrWhiteSpace(HourlyNote);
    public bool ShowInCreationNote => !string.IsNullOrWhiteSpace(InCreationNote);
    public bool ShowBarUnavailable => !ShowBar && !string.IsNullOrWhiteSpace(BarUnavailableReason);
}

/// <summary>
/// Live whole-project remaining-to-submit arithmetic. Unknown stays unknown; never coerced to zero.
/// </summary>
public static class BillingProjectFinancialSummaryCalculator
{
    public const string UnavailableFee = "לא זמין";
    public const string UnavailableBalance = "לא זמינה";
    public const string UnknownSnapshotDate = "תאריך snapshot לא ידוע";
    public const string HourlyMixNote =
        "יש רכיבי שעות (FeeType=4) בלי תקרת שכר טרחה נפרדת. סה\"כ המוצג הוא FeeSum ברמת הפרויקט ב-MasterPlan.";
    public const string HourlyNoFeeNote =
        "לרכיבי שעות אין סה\"כ שכר טרחה סופי ידוע — לא הומצא סכום כולל.";
    public const string PartialAmountBlocksAfter =
        "סכום החשבון חלקי, לכן יתרה אחרי והפס לא מחושבים כאילו החסר הוא אפס.";
    public const string MissingTotalBlocksBar =
        "אין סה\"כ שכר טרחה סופי, לכן לא מוצג פס 100%.";
    public const string MissingBalanceBlocksBar =
        "אין יתרה להגשה מה-snapshot, לכן לא מוצג פס 100%.";
    public const string NonPositiveFeeBlocksBar =
        "סה\"כ שכר טרחה אינו חיובי, לכן לא מוצג פס 100%.";
    public const string NegativeConsumedBlocksBar =
        "היתרה להגשה גדולה מסה\"כ שכר הטרחה, לכן לא מוצג פס 100%.";
    public const string ExceedsBalanceBlocksBar =
        "החשבון הזה גדול מהיתרה להגשה, לכן לא מוצג פס 100%.";

    public static readonly BillingProjectFinancialSummary Empty = Build(
        totalFee: null,
        balanceBefore: null,
        snapshotDate: null,
        feeMix: null,
        billsInCreation: 0,
        currentBill: 0m,
        currentBillIsComplete: false,
        hasCurrentBill: false);

    public static BillingProjectFinancialSummary Build(
        decimal? totalFee,
        decimal? balanceBefore,
        DateTime? snapshotDate,
        BillingSnapshotFeeMix? feeMix,
        int billsInCreation,
        decimal currentBill,
        bool currentBillIsComplete,
        bool hasCurrentBill = true)
    {
        var fee = RoundMoney(totalFee);
        var before = RoundMoney(balanceBefore);
        decimal? already = fee is decimal tf && before is decimal bb
            ? RoundMoney(tf - bb)
            : null;
        decimal? after = before is decimal bb2 && hasCurrentBill && currentBillIsComplete
            ? RoundMoney(bb2 - currentBill)
            : null;
        var currentText = hasCurrentBill
            ? BillingMoneyFormatter.FormatShekels(currentBill)
            : UnavailableFee;

        string? barReason = null;
        var showBar = false;
        if (!hasCurrentBill || !currentBillIsComplete)
            barReason = PartialAmountBlocksAfter;
        else if (fee is null)
            barReason = MissingTotalBlocksBar;
        else if (before is null)
            barReason = MissingBalanceBlocksBar;
        else if (fee <= 0m)
            barReason = NonPositiveFeeBlocksBar;
        else if (already is < 0m)
            barReason = NegativeConsumedBlocksBar;
        else if (after is < 0m)
            barReason = ExceedsBalanceBlocksBar;
        else
            showBar = true;

        var hourlyNote = feeMix is BillingSnapshotFeeMix.Hourly or BillingSnapshotFeeMix.Mixed
            ? (fee is null && feeMix == BillingSnapshotFeeMix.Hourly ? HourlyNoFeeNote : HourlyMixNote)
            : null;

        string? exceeds = null;
        if (after is < 0m && before is decimal)
        {
            exceeds = "אזהרה: החשבון הזה גדול מהיתרה להגשה. יתרה אחרי: "
                + BillingMoneyFormatter.FormatShekels(after.Value);
        }

        string? inCreation = billsInCreation > 0
            ? "יתרת MasterPlan אינה מנכה חשבונות בסטטוס ביצירה ("
              + billsInCreation.ToString(CultureInfo.InvariantCulture)
              + ")."
            : null;

        return new BillingProjectFinancialSummary(
            TotalFeeText: fee is decimal f ? BillingMoneyFormatter.FormatShekels(f) : UnavailableFee,
            BalanceBeforeText: before is decimal b ? BillingMoneyFormatter.FormatShekels(b) : UnavailableBalance,
            AlreadyBilledText: already is decimal a ? BillingMoneyFormatter.FormatShekels(a) : UnavailableFee,
            CurrentBillText: currentText,
            BalanceAfterText: after is decimal z ? BillingMoneyFormatter.FormatShekels(z) : UnavailableBalance,
            TotalFee: fee,
            BalanceBefore: before,
            AlreadyBilled: already,
            CurrentBill: hasCurrentBill ? currentBill : null,
            BalanceAfter: after,
            ObservedBarShare: showBar ? already ?? 0m : 0m,
            AdditionBarShare: showBar ? currentBill : 0m,
            RemainingBarShare: showBar ? after ?? 0m : 0m,
            ShowBar: showBar,
            BarUnavailableReason: showBar ? null : barReason,
            SourceLine: FormatSourceLine(snapshotDate),
            ExceedsBalanceWarning: exceeds,
            HourlyNote: hourlyNote,
            InCreationNote: inCreation);
    }

    public static string FormatSourceLine(DateTime? snapshotDate) =>
        snapshotDate is DateTime date
            ? "יתרה לפי snapshot MasterPlan מ־" + date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : "יתרה לפי snapshot MasterPlan מ־" + UnknownSnapshotDate;

    private static decimal? RoundMoney(decimal? value) =>
        value is decimal amount ? decimal.Round(amount, 2, MidpointRounding.AwayFromZero) : null;
}
