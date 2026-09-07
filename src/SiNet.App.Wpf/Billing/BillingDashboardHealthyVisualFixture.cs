using SiNet.Application.Billing;

namespace SiNet.App.Wpf.Billing;

/// <summary>
/// DEBUG visual-validation fixture. Does not replace production
/// <see cref="IBillingDashboardReadService"/> registration and does not read Replica.
/// </summary>
internal static class BillingDashboardHealthyVisualFixture
{
    private static readonly DateTime SnapshotDate = new(2026, 8, 2);
    private static readonly DateTime LastSync = new(2026, 9, 7, 7, 40, 0);

    public static IBillingDashboardReadService CreateService(
        IBillingReviewDecisionStore? localDecisions = null) =>
        new FakeService(CreateHealthyResult(), localDecisions);

    /// <summary>
    /// In-memory write session for the DEBUG Healthy fixture. Does not touch SiNet SQL or MasterPlan.
    /// </summary>
    internal sealed class LocalDecisionSession : IBillingReviewDecisionService, IBillingReviewDecisionStore
    {
        private readonly Dictionary<int, BillingReviewDecisionRecord> _byProject = new();
        private readonly TimeProvider _time;

        public LocalDecisionSession(TimeProvider? timeProvider = null)
        {
            _time = timeProvider ?? TimeProvider.System;
        }

        public Task<IReadOnlyList<BillingReviewDecisionRecord>> GetByProjectIdsAsync(
            IReadOnlyList<int> projectIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<BillingReviewDecisionRecord> rows = _byProject.Values
                .Where(r => projectIds.Contains(r.ProjectId))
                .ToList();
            return Task.FromResult(rows);
        }

        public Task SaveAsync(
            BillingReviewDecisionWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.DecisionType == BillingLocalDecisionType.NotNow
                && string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new ArgumentException("NotNow requires a reason.", nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var utcNow = _time.GetUtcNow().UtcDateTime;
            if (_byProject.TryGetValue(request.ProjectId, out var existing))
            {
                _byProject[request.ProjectId] = existing with
                {
                    DecisionType = request.DecisionType,
                    Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                    ReviewAgainDate = request.ReviewAgainDate?.Date,
                    UpdatedAtUtc = utcNow,
                    UpdatedByUserId = 0,
                    UpdatedByLogin = "Healthy fixture",
                    ClearedAtUtc = null,
                    ClearedByUserId = null,
                    ClearedByLogin = null,
                    ObservedLatestBillId = request.ObservedLatestBillId,
                    ObservedLatestBillStatusId = request.ObservedLatestBillStatusId,
                    ObservedLastBillDate = request.ObservedLastBillDate,
                    ObservedHoursSinceLastBill = request.ObservedHoursSinceLastBill,
                    ObservedLastWorkDate = request.ObservedLastWorkDate
                };
            }
            else
            {
                _byProject[request.ProjectId] = new BillingReviewDecisionRecord(
                    request.ProjectId,
                    request.DecisionType,
                    string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                    request.ReviewAgainDate?.Date,
                    utcNow,
                    0,
                    "Healthy fixture",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    request.ObservedLatestBillId,
                    request.ObservedLatestBillStatusId,
                    request.ObservedLastBillDate,
                    request.ObservedHoursSinceLastBill,
                    request.ObservedLastWorkDate);
            }

            return Task.CompletedTask;
        }

        public Task ClearAsync(int projectId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_byProject.TryGetValue(projectId, out var existing))
                return Task.CompletedTask;

            var utcNow = _time.GetUtcNow().UtcDateTime;
            _byProject[projectId] = existing with
            {
                ClearedAtUtc = utcNow,
                ClearedByUserId = 0,
                ClearedByLogin = "Healthy fixture",
                UpdatedAtUtc = utcNow,
                UpdatedByUserId = 0,
                UpdatedByLogin = "Healthy fixture"
            };
            return Task.CompletedTask;
        }
    }

    public static BillingDashboardResult CreateHealthyResult()
    {
        var fixedFee = Fee(BillingSnapshotFeeMix.FixedPrice, MasterPlanSnapshotFeeTypeIds.FixedPrice, "מחיר קבוע", 2);
        var hourlyFee = Fee(BillingSnapshotFeeMix.Hourly, MasterPlanSnapshotFeeTypeIds.WorkingHours, "שעות עבודה", 3);
        var mixedFee = new BillingFeeTypeSummary(
            [MasterPlanSnapshotFeeTypeIds.FixedPrice, MasterPlanSnapshotFeeTypeIds.WorkingHours],
            [
                new BillingFeeTypeCount(MasterPlanSnapshotFeeTypeIds.FixedPrice, "מחיר קבוע", 1),
                new BillingFeeTypeCount(MasterPlanSnapshotFeeTypeIds.WorkingHours, "שעות עבודה", 2)
            ],
            BillingSnapshotFeeMix.Mixed);

        var rows = new[]
        {
            Row(5905, "5905", "גשר הצפון — תכנון מפורט והשלמות קונסטרוקציה", "לקוח רפליקה",
                BillingCandidateState.ReviewNow, "32 שעות מאז החשבון האחרון; עבודה ב-30 הימים האחרונים.",
                hours30: 18.5m, hoursSince: 32m, daysSince: 41, lastBill: new DateTime(2026, 7, 28),
                lastBillStatus: "הוגש", snapshotBalance: 18450m, snapshotOpen: 4200m, billed: 38m,
                snapshotDate: SnapshotDate, fees: fixedFee, workDays: 6, feeSum: 96000m),
            Row(5893, "5893", "מגדל הסיטי — ליווי ביצוע ושאלות קבלן", "עיריית חיפה",
                BillingCandidateState.BillInPreparation, "חשבון ביצירה; אין צורך לפתוח חשבון חדש עכשיו.",
                hours30: 9m, hoursSince: 9m, daysSince: 12, lastBill: new DateTime(2026, 8, 26),
                lastBillStatus: "ביצירה", snapshotBalance: 2100m, snapshotOpen: 2100m, billed: 12m,
                snapshotDate: SnapshotDate, fees: hourlyFee, billsInCreation: 1, workDays: 4, feeSum: 42000m),
            Row(3611, "3611", "כביש עוקף — בדיקות קרקע והשלמת מדידות", "לקוח אחר",
                BillingCandidateState.AccumulatedWork, "אין שעות ב-30 הימים האחרונים, אך נצברו שעות מאז החשבון.",
                hours30: 0m, hoursSince: 14.25m, daysSince: 96, lastBill: new DateTime(2026, 6, 3),
                lastBillStatus: "מאושר", snapshotBalance: null, snapshotOpen: null, billed: null,
                snapshotDate: null, fees: mixedFee, workDays: 0, feeSum: 27500m),
            Row(6982, "6982", "כיסוי גגות — פיקוח עליון", "לקוח ג",
                BillingCandidateState.CoveredByLatestBill, "אין שעות חדשות מאז החשבון האחרון.",
                hours30: 0m, hoursSince: 0m, daysSince: 8, lastBill: new DateTime(2026, 8, 30),
                lastBillStatus: "מאושר", snapshotBalance: 0m, snapshotOpen: 0m, billed: 100m,
                snapshotDate: SnapshotDate, fees: fixedFee, workDays: 0, feeSum: 15000m),
            Row(6120, "6120", "שיקום מבנה מגורים ברחוב הרצל — ליווי מהנדס אחראי כולל תיאום מול ועדה מקומית", null,
                BillingCandidateState.ReviewNow, "לקוח חסר ברפליקה; 21 שעות חדשות אחרי חשבון.",
                hours30: 21m, hoursSince: 21m, daysSince: 19, lastBill: new DateTime(2026, 8, 19),
                lastBillStatus: "הוגש", snapshotBalance: 7300m, snapshotOpen: 1800m, billed: 22m,
                snapshotDate: SnapshotDate, fees: hourlyFee, workDays: 8, feeSum: 54000m),
            Row(5771, "5771", "מחלף דרום — תנועה ותיאום תשתיות", "נתיבי ישראל",
                BillingCandidateState.ReviewNow, "עבודה רציפה בשלושה שבועות אחרונים.",
                hours30: 44m, hoursSince: 61m, daysSince: 53, lastBill: new DateTime(2026, 7, 16),
                lastBillStatus: "הוגש", snapshotBalance: 41200m, snapshotOpen: 9500m, billed: 47m,
                snapshotDate: SnapshotDate, fees: mixedFee, workDays: 12, feeSum: 188000m),
            Row(5402, "5402", "בית ספר יסודי — פיקוח שלד וגמר", "משרד החינוך",
                BillingCandidateState.AccumulatedWork, "העבודה נעצרה לפני יותר מ-30 יום אך נותרו שעות לא מחויבות.",
                hours30: 0m, hoursSince: 27.5m, daysSince: 74, lastBill: new DateTime(2026, 6, 25),
                lastBillStatus: "מאושר", snapshotBalance: 5600m, snapshotOpen: null, billed: 55m,
                snapshotDate: SnapshotDate, fees: fixedFee, workDays: 0, feeSum: 31000m),
            Row(4990, "4990", "קו ביוב ראשי — מדידות As-Made", "תאגיד המים",
                BillingCandidateState.BillInPreparation, "חשבון ביצירה על שעות אוגוסט.",
                hours30: 6m, hoursSince: 6m, daysSince: 5, lastBill: new DateTime(2026, 9, 2),
                lastBillStatus: "ביצירה", snapshotBalance: 890m, snapshotOpen: 890m, billed: 8m,
                snapshotDate: SnapshotDate, fees: hourlyFee, billsInCreation: 1, workDays: 2, feeSum: 18500m),
            Row(4815, "4815", "הרחבת אולם ספורט עירוני — תכנון קונסטרוקציה וליווי מכרז קבלנים", "המועצה האזורית",
                BillingCandidateState.ReviewNow, "שעות רבות מאז החשבון האחרון.",
                hours30: 12m, hoursSince: 48m, daysSince: 62, lastBill: new DateTime(2026, 7, 7),
                lastBillStatus: "הוגש", snapshotBalance: 22600m, snapshotOpen: 0m, billed: 61m,
                snapshotDate: SnapshotDate, fees: mixedFee, workDays: 5, feeSum: 74000m),
            Row(4308, "4308", "שימור חזיתות — ליווי אדריכלי", "לקוח פרטי",
                BillingCandidateState.CoveredByLatestBill, "החשבון האחרון מכסה את העבודה הנוכחית.",
                hours30: 2m, hoursSince: 0m, daysSince: 4, lastBill: new DateTime(2026, 9, 3),
                lastBillStatus: "מאושר", snapshotBalance: 150m, snapshotOpen: 0m, billed: 92m,
                snapshotDate: SnapshotDate, fees: fixedFee, workDays: 1, feeSum: 9800m),
            Row(3901, "3901", "פארק עירוני — פיתוח נופי ושבילי אופניים לאורך נחל הקישון כולל תיאום רשות ניקוז", null,
                BillingCandidateState.AccumulatedWork, "שם לקוח חסר; נצברו 11 שעות ישנות.",
                hours30: 0m, hoursSince: 11m, daysSince: 110, lastBill: new DateTime(2026, 5, 20),
                lastBillStatus: "הוגש", snapshotBalance: null, snapshotOpen: 3200m, billed: null,
                snapshotDate: null, fees: hourlyFee, workDays: 0, feeSum: 22000m),
            Row(3550, "3550", "מנהרת הולכי רגל — פיקוח בטיחות", "רכבת ישראל",
                BillingCandidateState.ReviewNow, "עבודה השבוע אחרי חשבון יולי.",
                hours30: 15.75m, hoursSince: 19m, daysSince: 33, lastBill: new DateTime(2026, 8, 5),
                lastBillStatus: "הוגש", snapshotBalance: 9900m, snapshotOpen: 2500m, billed: 29m,
                snapshotDate: SnapshotDate, fees: mixedFee, workDays: 7, feeSum: 67500m),
            Row(3217, "3217", "מגרש חניה תת-קרקעי", "חברת ניהול",
                BillingCandidateState.CoveredByLatestBill, "אין שעות מאז החשבון שאושר.",
                hours30: 0m, hoursSince: 0m, daysSince: 21, lastBill: new DateTime(2026, 8, 17),
                lastBillStatus: "מאושר", snapshotBalance: 440m, snapshotOpen: 0m, billed: 88m,
                snapshotDate: SnapshotDate, fees: fixedFee, workDays: 0, feeSum: 12000m)
        };

        var summary = new BillingDashboardSummary(
            ReviewNowCount: rows.Count(r => r.CandidateState == BillingCandidateState.ReviewNow),
            BillInPreparationCount: rows.Count(r => r.CandidateState == BillingCandidateState.BillInPreparation),
            SubmittedOpenAmount: null,
            ReceivedThisMonth: 187650.00m,
            ReplicaLastSyncTime: LastSync,
            MonthlySnapshotDate: SnapshotDate,
            AccumulatedWorkCount: rows.Count(r => r.CandidateState == BillingCandidateState.AccumulatedWork),
            CoveredByLatestBillCount: rows.Count(r => r.CandidateState == BillingCandidateState.CoveredByLatestBill),
            NotUrgentCount: 0);

        var freshness = new BillingSourceFreshness(
            LastSync, LastSync, LastSync, LastSync, LastSync, LastSync, new DateTime(2026, 9, 6), SnapshotDate);

        return new BillingDashboardResult(
            summary,
            rows,
            freshness,
            [],
            new ReplicaConnectionDiagnostics(
                "SI-WIN-2K19\\SIDATA", "Replica_DB", "SI-WIN-2K19", "SI-WIN-2K19", "SIDATA", "Replica_DB",
                LastSync, LastSync, LastSync, LastSync, LastSync,
                new DateTime(2026, 9, 6), LastSync, LastSync, LastSync),
            BillingReplicaFreshnessStatus.Healthy,
            CandidatesBlocked: false);
    }

    private static BillingFeeTypeSummary Fee(BillingSnapshotFeeMix mix, int id, string name, int count) =>
        new([id], [new BillingFeeTypeCount(id, name, count)], mix);

    private static BillingCandidateRow Row(
        int id,
        string number,
        string name,
        string? customer,
        BillingCandidateState state,
        string reason,
        decimal hours30,
        decimal hoursSince,
        int daysSince,
        DateTime lastBill,
        string lastBillStatus,
        decimal? snapshotBalance,
        decimal? snapshotOpen,
        decimal? billed,
        DateTime? snapshotDate,
        BillingFeeTypeSummary fees,
        int workDays,
        decimal feeSum,
        int billsInCreation = 0) =>
        new(
            id,
            number,
            name,
            customer,
            "פעיל",
            feeSum,
            hours30 > 0 ? new DateTime(2026, 9, 4) : lastBill.AddDays(3),
            hours30,
            hours30 + 8,
            hours30 + 16,
            hoursSince,
            workDays,
            lastBill,
            daysSince,
            id * 10,
            "B-" + number,
            lastBillStatus == "ביצירה" ? 1 : lastBillStatus == "הוגש" ? 2 : 3,
            lastBillStatus,
            hoursSince * 420m,
            billsInCreation,
            lastBillStatus == "הוגש" ? 1 : 0,
            lastBillStatus == "מאושר" ? 1 : 0,
            billsInCreation + (lastBillStatus == "הוגש" ? 1 : 0),
            snapshotBalance,
            snapshotOpen,
            billed is decimal p ? Math.Round(feeSum * p / 100m, 2) : null,
            billed,
            snapshotDate,
            fees,
            state,
            reason);

    private sealed class FakeService(
        BillingDashboardResult result,
        IBillingReviewDecisionStore? localDecisions) : IBillingDashboardReadService
    {
        public async Task<BillingDashboardResult> GetAsync(
            BillingDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (localDecisions is null || result.Candidates.Count == 0)
                return result;

            var stored = await localDecisions
                .GetByProjectIdsAsync(result.Candidates.Select(r => r.ProjectId).ToList(), cancellationToken)
                .ConfigureAwait(false);
            var asOf = (request.AsOfDate ?? DateTime.Today).Date;
            var rows = BillingLocalDecisionApplier.Apply(result.Candidates, stored, asOf);
            return result with { Candidates = rows };
        }
    }
}
