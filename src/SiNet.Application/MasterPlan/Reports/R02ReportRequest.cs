namespace SiNet.Application.MasterPlan.Reports;

/// <summary>R02 hours report request.</summary>
public sealed record R02ReportRequest(
    DateTime StartDate,
    DateTime EndDate,
    IReadOnlyList<int>? ProjectIds = null,
    IReadOnlyList<int>? CustomerIds = null,
    IReadOnlyList<int>? EmployeeIds = null,
    bool ActiveProjectsOnly = false,
    bool ActiveEmployeesOnly = true,
    bool ExcludeZeroHours = true,
    bool IsClientExport = false)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (EndDate < StartDate)
            errors.Add("תאריך סיום לפני תאריך התחלה.");
        if ((EndDate - StartDate).TotalDays > 366 * 2)
            errors.Add("טווח התאריכים ארוך מדי (מקסימום שנתיים).");
        return errors;
    }
}
