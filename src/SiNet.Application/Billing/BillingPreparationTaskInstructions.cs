using System.Globalization;
using System.Text;

namespace SiNet.Application.Billing;

public static class BillingPreparationTaskInstructions
{
    public const string RequestIdentityPrefix = "Billing Preparation Request #";

    public static string RequestIdentityMarker(int requestId) =>
        RequestIdentityPrefix + requestId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// True when <paramref name="body"/> identifies exactly this preparation request.
    /// Digit-bounded so <c>#2</c> does not match <c>#21</c>.
    /// </summary>
    public static bool BodyIdentifiesRequest(string? body, int requestId)
    {
        if (string.IsNullOrEmpty(body) || requestId <= 0)
            return false;

        var marker = RequestIdentityMarker(requestId);
        var start = 0;
        while (true)
        {
            var index = body.IndexOf(marker, start, StringComparison.Ordinal);
            if (index < 0)
                return false;

            var after = index + marker.Length;
            if (after >= body.Length || !char.IsAsciiDigit(body[after]))
                return true;

            start = after;
        }
    }

    public static string Build(BillingPreparationRequestRecord request) =>
        Build(request, stageCatalog: null, hourlyCatalog: null);

    public static string Build(
        BillingPreparationRequestRecord request,
        IReadOnlyList<BillingPreparationStageDraft>? stageCatalog,
        IReadOnlyList<BillingHourlySubContractDraft>? hourlyCatalog)
    {
        ArgumentNullException.ThrowIfNull(request);

        var titleNumber = request.ProjectNumber ?? request.MasterPlanProjectId.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("הכנת חשבון — פרויקט ").Append(titleNumber).AppendLine();
        if (!string.IsNullOrWhiteSpace(request.ProjectName))
            sb.AppendLine(request.ProjectName.Trim());
        sb.AppendLine();

        if (BillingPreparationPricingFreeze.HasFreeze(request))
            AppendFrozenMoneySummary(sb, request);
        else if (stageCatalog is not null || hourlyCatalog is not null)
            AppendLiveMoneySummary(sb, request, stageCatalog ?? [], hourlyCatalog ?? []);

        sb.AppendLine("יש לבצע ב-MasterPlan:");
        sb.AppendLine();

        if (request.ManualOverride)
        {
            sb.AppendLine("טיפול ידני אושר — נתוני שלבי MasterPlan לא היו זמינים.");
            if (!string.IsNullOrWhiteSpace(request.ManualOverrideReason))
                sb.Append("סיבה: ").AppendLine(request.ManualOverrideReason.Trim());
            sb.AppendLine();
        }

        var stages = request.Stages.Where(s => s.RequestedDelta > 0m).ToList();
        if (stages.Count > 0)
        {
            sb.AppendLine("שלבים:");
            foreach (var group in stages.GroupBy(s => s.MasterPlanSubContractId))
            {
                var first = group.First();
                sb.Append("תת חוזה: ").AppendLine(first.SubContractName);
                foreach (var stage in group)
                {
                    sb.Append("• ").AppendLine(stage.StageName);
                    sb.Append("  משקל השלב בתת החוזה: ")
                        .Append(Percent(stage.StageWeightWithinSubContract))
                        .AppendLine("%");
                    sb.Append("  חויב בזמן האישור: ");
                    sb.AppendLine(stage.ObservedCumulativeProgress is decimal o
                        ? Percent(o) + "%"
                        : "לא ידוע");
                    sb.Append("  להוסיף בחשבון הזה: ").Append(Percent(stage.RequestedDelta)).AppendLine("%");
                    sb.Append("  לאחר החשבון: ").Append(Percent(stage.TargetCumulativeProgress)).AppendLine("%");
                    sb.Append("  תרומת התוספת לתת החוזה: ")
                        .Append(BillingStageContributionCalculator
                            .SubContractContributionPercent(stage.StageWeightWithinSubContract, stage.RequestedDelta)
                            .ToString("0.##", CultureInfo.InvariantCulture))
                        .AppendLine("%");
                    AppendStageMoney(sb, stage, stageCatalog, BillingPreparationPricingFreeze.HasFreeze(request));
                }
            }

            sb.AppendLine();
        }

        if (request.Hours.Count > 0)
        {
            sb.AppendLine("שעות:");
            foreach (var group in BillingPreparationHoursScopeComposer.GroupCompatibleScopes(request.Hours))
            {
                var first = group[0];
                sb.Append("תקופה: ");
                sb.Append(first.FromDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                sb.Append('–');
                sb.AppendLine(first.ToDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                sb.AppendLine("הסכמי משנה:");
                foreach (var hours in group)
                {
                    sb.Append("• ").Append(hours.SubContractName);
                    sb.Append(" — ").Append(hours.ReportCount.ToString(CultureInfo.InvariantCulture));
                    sb.Append(" דיווחים, ");
                    sb.Append(hours.TotalHours.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.AppendLine(" שעות");
                    AppendHoursMoney(sb, hours, hourlyCatalog, BillingPreparationPricingFreeze.HasFreeze(request));
                }

                sb.AppendLine("סה\"כ:");
                sb.Append(group.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" הסכמי משנה");
                sb.Append(group.Sum(h => h.ReportCount).ToString(CultureInfo.InvariantCulture)).AppendLine(" דיווחים");
                sb.Append(group.Sum(h => h.TotalHours).ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine(" שעות");
                sb.AppendLine();
            }
        }

        sb.Append("מקור החלטה: ").Append(RequestIdentityMarker(request.Id)).AppendLine();
        if (!string.IsNullOrWhiteSpace(request.ApprovedByLogin))
            sb.Append("אושר על ידי ").AppendLine(request.ApprovedByLogin);
        return sb.ToString().TrimEnd();
    }

    private static void AppendFrozenMoneySummary(StringBuilder sb, BillingPreparationRequestRecord request)
    {
        if (request.PricingIsPartial == true)
            sb.Append("סכום מחושב חלקית: ");
        else
            sb.Append("סה\"כ להכנת חשבון: ");
        sb.AppendLine(BillingMoneyFormatter.FormatShekels(request.PricingTotal ?? 0m));
        sb.Append("שלבי תשלום: ").AppendLine(BillingMoneyFormatter.FormatShekels(request.PricingStageTotal ?? 0m));
        sb.Append("שעות: ").AppendLine(BillingMoneyFormatter.FormatShekels(request.PricingHoursTotal ?? 0m));
        if (request.PricingIsPartial == true)
        {
            var missing = BillingPreparationPricingFreeze.ListMissing(request);
            sb.Append(missing.Count.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" רכיבים אינם כלולים בסכום");
            foreach (var line in missing)
                sb.Append("⚠ ").AppendLine(line);
        }

        sb.AppendLine();
    }

    private static void AppendLiveMoneySummary(
        StringBuilder sb,
        BillingPreparationRequestRecord request,
        IReadOnlyList<BillingPreparationStageDraft> stageCatalog,
        IReadOnlyList<BillingHourlySubContractDraft> hourlyCatalog)
    {
        var stages = request.Stages
            .Where(s => s.RequestedDelta > 0m)
            .Select(s => (ResolveStageDraft(s, stageCatalog), s.RequestedDelta));
        var hours = request.Hours.Select(h =>
        {
            var draft = hourlyCatalog.FirstOrDefault(c => c.MasterPlanSubContractId == h.MasterPlanSubContractId)
                        ?? new BillingHourlySubContractDraft(
                            h.MasterPlanSubContractId,
                            h.SubContractName,
                            MasterPlanSnapshotFeeTypeIds.WorkingHours,
                            AmountUnavailableReason: "לא נמצא בסיס תמחור שעתי");
            return (draft, h.TotalHours, h.SubContractName);
        });
        var summary = BillingPreparationAmountCalculator.Summarize(stages, hours);
        if (summary.IsPartial)
            sb.Append("סכום מחושב חלקית: ");
        else
            sb.Append("סה\"כ להכנת חשבון: ");
        sb.AppendLine(BillingMoneyFormatter.FormatShekels(summary.PricedTotal));
        sb.Append("שלבי תשלום: ").AppendLine(BillingMoneyFormatter.FormatShekels(summary.PricedStageTotal));
        sb.Append("שעות: ").AppendLine(BillingMoneyFormatter.FormatShekels(summary.PricedHoursTotal));
        if (summary.IsPartial)
        {
            sb.Append(summary.Missing.Count.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" רכיבים אינם כלולים בסכום");
            foreach (var missing in summary.Missing)
                sb.Append("⚠ ").Append(missing.Label).Append(" — ").AppendLine(missing.Reason);
        }

        sb.AppendLine();
    }

    private static void AppendStageMoney(
        StringBuilder sb,
        BillingPreparationStageLineSnapshot stage,
        IReadOnlyList<BillingPreparationStageDraft>? catalog,
        bool useFrozen)
    {
        if (useFrozen || BillingPreparationPricingFreeze.HasLineEvidence(stage))
        {
            sb.Append("  תוספת כספית: ");
            sb.AppendLine(stage.PricingCalculatedAmount is decimal amount
                ? BillingMoneyFormatter.FormatShekels(amount)
                : stage.PricingUnavailableReason ?? "לא נמצא בסיס תמחור");
            return;
        }

        if (catalog is null)
            return;
        var priced = BillingPreparationAmountCalculator.StageAddition(
            ResolveStageDraft(stage, catalog), stage.RequestedDelta);
        sb.Append("  תוספת כספית: ");
        sb.AppendLine(priced.IsPriced
            ? BillingMoneyFormatter.FormatShekels(priced.Amount!.Value)
            : priced.UnavailableReason ?? "לא נמצא בסיס תמחור");
    }

    private static void AppendHoursMoney(
        StringBuilder sb,
        BillingPreparationHoursLineSnapshot hours,
        IReadOnlyList<BillingHourlySubContractDraft>? catalog,
        bool useFrozen)
    {
        if (useFrozen || BillingPreparationPricingFreeze.HasLineEvidence(hours))
        {
            if (hours.PricingHourlyRate is decimal frozenRate)
                sb.Append("  תעריף: ").AppendLine(BillingMoneyFormatter.FormatShekels(frozenRate));
            sb.Append("  סכום שעות: ");
            sb.AppendLine(hours.PricingCalculatedAmount is decimal amount
                ? BillingMoneyFormatter.FormatShekels(amount)
                : hours.PricingUnavailableReason ?? "תעריף לא ניתן לקביעה");
            return;
        }

        if (catalog is null)
            return;
        var draft = catalog.FirstOrDefault(c => c.MasterPlanSubContractId == hours.MasterPlanSubContractId)
                    ?? new BillingHourlySubContractDraft(
                        hours.MasterPlanSubContractId,
                        hours.SubContractName,
                        MasterPlanSnapshotFeeTypeIds.WorkingHours,
                        AmountUnavailableReason: "לא נמצא בסיס תמחור שעתי");
        if (draft.UniqueHourlyRate is decimal rate)
            sb.Append("  תעריף: ").AppendLine(BillingMoneyFormatter.FormatShekels(rate));

        var priced = BillingPreparationAmountCalculator.Hourly(draft, hours.TotalHours);
        sb.Append("  סכום שעות: ");
        sb.AppendLine(priced.IsPriced
            ? BillingMoneyFormatter.FormatShekels(priced.Amount!.Value)
            : priced.UnavailableReason ?? "תעריף לא ניתן לקביעה");
    }

    private static BillingPreparationStageDraft ResolveStageDraft(
        BillingPreparationStageLineSnapshot stage,
        IReadOnlyList<BillingPreparationStageDraft> catalog)
    {
        var match = catalog.FirstOrDefault(d => d.MasterPlanStageId == stage.MasterPlanStageId);
        if (match is not null)
            return match with { StageWeightWithinSubContract = stage.StageWeightWithinSubContract };
        return new BillingPreparationStageDraft(
            stage.MasterPlanStageId,
            stage.MasterPlanSubContractId,
            stage.StageName,
            stage.SubContractName,
            stage.StageWeightWithinSubContract,
            new BillingStageProgressCalculator.ObservedCumulativeProgress(
                stage.ObservedCumulativeProgress, stage.HasDataQualityFlag, []),
            stage.ObservedCumulativeProgress,
            true);
    }

    private static string Percent(decimal fraction) =>
        (fraction * 100m).ToString("0.##", CultureInfo.InvariantCulture);
}
