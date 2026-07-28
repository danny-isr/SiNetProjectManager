namespace SiNet.Application.MasterPlan.Reports;

/// <summary>R03 attendance-comparison generate request (Replica data → Google Sheet).</summary>
public sealed record R03ReportRequest(
    int Year,
    int Month,
    IReadOnlyList<int> EmployeeIds,
    bool ActiveEmployeesOnly = true)
{
    private static readonly string[] HebrewMonths =
    [
        "", "ינואר", "פברואר", "מרץ", "אפריל", "מאי", "יוני",
        "יולי", "אוגוסט", "ספטמבר", "אוקטובר", "נובמבר", "דצמבר",
    ];

    public DateTime StartDate => new(Year, Month, 1);

    public DateTime EndDate => StartDate.AddMonths(1).AddDays(-1);

    public string MonthDisplayName =>
        Month is >= 1 and <= 12 ? HebrewMonths[Month] : Month.ToString();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Month is < 1 or > 12)
            errors.Add("חודש לא תקין.");
        if (Year is < 2020 or > 2100)
            errors.Add("שנה לא תקינה.");
        return errors;
    }
}
