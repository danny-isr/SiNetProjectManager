using System.Globalization;
using System.Text;

namespace SiNet.Application.Billing;

public static class BillingPreparationTaskInstructions
{
    public static string Build(BillingPreparationRequestRecord request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var titleNumber = request.ProjectNumber ?? request.MasterPlanProjectId.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("הכנת חשבון — פרויקט ").Append(titleNumber).AppendLine();
        if (!string.IsNullOrWhiteSpace(request.ProjectName))
            sb.AppendLine(request.ProjectName.Trim());
        sb.AppendLine();
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
                }

                sb.AppendLine("סה\"כ:");
                sb.Append(group.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" הסכמי משנה");
                sb.Append(group.Sum(h => h.ReportCount).ToString(CultureInfo.InvariantCulture)).AppendLine(" דיווחים");
                sb.Append(group.Sum(h => h.TotalHours).ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine(" שעות");
                sb.AppendLine();
            }
        }

        sb.Append("מקור החלטה: Billing Preparation Request #").Append(request.Id).AppendLine();
        if (!string.IsNullOrWhiteSpace(request.ApprovedByLogin))
            sb.Append("אושר על ידי ").AppendLine(request.ApprovedByLogin);
        return sb.ToString().TrimEnd();
    }

    private static string Percent(decimal fraction) =>
        (fraction * 100m).ToString("0.##", CultureInfo.InvariantCulture);
}
