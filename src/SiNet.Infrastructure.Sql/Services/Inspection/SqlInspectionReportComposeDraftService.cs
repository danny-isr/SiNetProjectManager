using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Inspection;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Inspection;

/// <summary>
/// Builds an Inspection→planner email draft from SQL without Gmail/PDF/send side effects.
/// </summary>
internal sealed class SqlInspectionReportComposeDraftService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory) : IInspectionReportComposeDraftService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<InspectionReportComposeDraft?> BuildDraftAsync(
        int reportId,
        CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var report = await db.InspectionReports
            .AsNoTracking()
            .Include(r => r.Project)
                .ThenInclude(p => p!.ProjectPlanners)
                    .ThenInclude(pp => pp.Contacts)
            .Include(r => r.Inspector)
            .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken)
            .ConfigureAwait(false);

        if (report is null)
            return null;

        var plannerEmails = report.Project?.ProjectPlanners
            .Select(pp => pp.Contacts?.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        var subjectProject = !string.IsNullOrWhiteSpace(report.Project?.Title)
            ? report.Project!.Title!
            : report.Project?.NameAndNumber ?? $"פרויקט {report.ProjectId}";

        var inspectorName = !string.IsNullOrWhiteSpace(report.InspectorName)
            ? report.InspectorName!
            : report.Inspector?.Name ?? report.Inspector?.LoginName ?? string.Empty;

        var sheetUrl = report.SentSpreadsheetUrl?.Trim();
        var sheetId = report.SentSpreadsheetId?.Trim();

        var body = $"שלום,{Environment.NewLine}{Environment.NewLine}" +
                   $"מצורף דוח בדיקה מס' {report.ReportNumber}.{Environment.NewLine}{Environment.NewLine}" +
                   $"קישור לדוח Google Sheets:{Environment.NewLine}" +
                   $"{(string.IsNullOrWhiteSpace(sheetUrl) ? "(טרם יוצא — יש לבצע ייצוא לפני שליחה)" : sheetUrl)}" +
                   $"{Environment.NewLine}{Environment.NewLine}" +
                   $"מילוי תגובת המתכנן ישירות בגיליון מזרז את תהליך הבדיקה.{Environment.NewLine}{Environment.NewLine}" +
                   $"בברכה,{Environment.NewLine}" +
                   $"{inspectorName}";

        string? warning = null;
        if (string.IsNullOrWhiteSpace(sheetId) && string.IsNullOrWhiteSpace(sheetUrl))
            warning = "יש לייצא את הדוח לפני שליחה למתכנן.";
        else if (plannerEmails.Count == 0)
            warning = "חסר נמען מתכנן (ProjectPlanners) — Send נשאר חסום עד להשלמת נמען.";

        return new InspectionReportComposeDraft(
            ReportId: report.ReportId,
            ReportNumber: report.ReportNumber,
            ProjectId: report.ProjectId,
            Subject: $"דוח בדיקה מס' {report.ReportNumber} - {subjectProject}",
            Body: body,
            ToRecipients: plannerEmails,
            SentSpreadsheetId: sheetId,
            SentSpreadsheetUrl: sheetUrl,
            WarningMessage: warning);
    }
}
