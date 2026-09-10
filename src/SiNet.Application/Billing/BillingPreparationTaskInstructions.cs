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

        if (request.Stages.Count > 0)
        {
            sb.AppendLine("שלבים:");
            foreach (var stage in request.Stages)
            {
                sb.Append("• ").Append(stage.SubContractName).Append(" / ").Append(stage.StageName);
                sb.Append(" — להגיש עד ");
                sb.Append(Percent(stage.TargetCumulativeProgress));
                sb.AppendLine("% מצטבר מהשלב");
                sb.Append("  מצב שנצפה בזמן האישור: ");
                sb.Append(stage.ObservedCumulativeProgress is decimal o ? Percent(o) + "%" : "לא ידוע");
                sb.AppendLine();
            }

            sb.AppendLine();
        }

        if (request.Hours.Count > 0)
        {
            sb.AppendLine("שעות:");
            foreach (var hours in request.Hours)
            {
                sb.Append("• הסכם משנה ").AppendLine(hours.SubContractName);
                sb.Append("• תקופה ");
                sb.Append(hours.FromDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                sb.Append('–');
                sb.AppendLine(hours.ToDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                sb.Append("• ").Append(hours.ReportCount).AppendLine(" דיווחים");
                sb.Append("• ").Append(hours.TotalHours.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine(" שעות");
            }

            sb.AppendLine();
        }

        sb.Append("מקור החלטה: Billing Preparation Request #").Append(request.Id).AppendLine();
        if (!string.IsNullOrWhiteSpace(request.ApprovedByLogin))
            sb.Append("אושר על ידי ").AppendLine(request.ApprovedByLogin);
        return sb.ToString().TrimEnd();
    }

    private static string Percent(decimal fraction) =>
        (fraction * 100m).ToString("0.##", CultureInfo.InvariantCulture);
}
