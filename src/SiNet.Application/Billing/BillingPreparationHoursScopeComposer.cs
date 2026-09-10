namespace SiNet.Application.Billing;

/// <summary>
/// Builds a manager-selected hourly scope from SubContract + inclusive dates.
/// Does not claim the hours were billed or unbilled in MasterPlan.
/// </summary>
public static class BillingPreparationHoursScopeComposer
{
    public const string ManagerSelectedScopeCaption =
        "היקף שעות שנבחר במפורש על ידי המנהל לפי הסכמי משנה ותאריכים. אינו קובע מה חויב ב-MasterPlan.";

    public static BillingHourlySubContractDraft RequireHourlySubContract(
        int masterPlanSubContractId,
        IReadOnlyList<BillingHourlySubContractDraft> hourlySubContracts)
    {
        ArgumentNullException.ThrowIfNull(hourlySubContracts);
        var match = hourlySubContracts.FirstOrDefault(s => s.MasterPlanSubContractId == masterPlanSubContractId);
        if (match is null)
            throw new InvalidOperationException("יש לבחור הסכם משנה שעתי מה-snapshot שנטען.");
        if (match.FeeTypeId != MasterPlanSnapshotFeeTypeIds.WorkingHours)
            throw new InvalidOperationException("הסכם המשנה שנבחר אינו שעתי (FeeType=4).");
        return match;
    }

    public static BillingPreparationHoursScopePreview Preview(
        int? masterPlanSubContractId,
        DateTime? fromDateInclusive,
        DateTime? toDateInclusive,
        IReadOnlyList<BillingHourlySubContractDraft> hourlySubContracts,
        IReadOnlyList<BillingHourReportFact> hourReports)
    {
        ArgumentNullException.ThrowIfNull(hourlySubContracts);
        ArgumentNullException.ThrowIfNull(hourReports);

        if (masterPlanSubContractId is not int subId || subId <= 0)
            return BillingPreparationHoursScopePreview.Empty();
        if (fromDateInclusive is not DateTime from || toDateInclusive is not DateTime to)
            return BillingPreparationHoursScopePreview.Empty();
        if (to.Date < from.Date)
            return BillingPreparationHoursScopePreview.Empty("עד תאריך חייב להיות באותו יום או אחרי מתאריך.");

        var match = hourlySubContracts.FirstOrDefault(s => s.MasterPlanSubContractId == subId);
        if (match is null)
            return BillingPreparationHoursScopePreview.Empty("יש לבחור הסכם משנה שעתי מה-snapshot שנטען.");
        if (match.FeeTypeId != MasterPlanSnapshotFeeTypeIds.WorkingHours)
            return BillingPreparationHoursScopePreview.Empty("הסכם המשנה שנבחר אינו שעתי (FeeType=4).");

        var reports = DistinctReports(
            BillingHourlyScopeResolver.Resolve(hourReports, subId, from.Date, to.Date));
        return new BillingPreparationHoursScopePreview(
            reports,
            reports.Count,
            reports.Sum(r => r.Hours),
            ValidationMessage: null);
    }

    public static BillingPreparationHoursLineSnapshot Compose(
        int masterPlanSubContractId,
        DateTime fromDateInclusive,
        DateTime toDateInclusive,
        IReadOnlyList<BillingHourlySubContractDraft> hourlySubContracts,
        IReadOnlyList<BillingHourReportFact> hourReports,
        IReadOnlyList<int> overlappingHourReportIds,
        DateTime snapshotTimestampUtc,
        BillingConfirmationMode confirmationMode = BillingConfirmationMode.None,
        DateTime? confirmedAtUtc = null,
        int? confirmedByUserId = null,
        string? confirmationNote = null)
    {
        var sub = RequireHourlySubContract(masterPlanSubContractId, hourlySubContracts);
        var preview = Preview(
            masterPlanSubContractId,
            fromDateInclusive,
            toDateInclusive,
            hourlySubContracts,
            hourReports);
        if (preview.ValidationMessage is not null)
            throw new InvalidOperationException(preview.ValidationMessage);
        if (preview.ReportCount == 0)
            throw new InvalidOperationException("לא ניתן לשמור היקף שעות ללא דיווחים תואמים בטווח שנבחר.");

        IReadOnlyList<int> overlap = overlappingHourReportIds is null
            ? Array.Empty<int>()
            : overlappingHourReportIds
                .Where(id => preview.Reports.Any(r => r.HoursReportId == id))
                .Distinct()
                .ToList();

        return new BillingPreparationHoursLineSnapshot(
            sub.MasterPlanSubContractId,
            sub.Name,
            fromDateInclusive.Date,
            toDateInclusive.Date,
            preview.ReportCount,
            preview.TotalHours,
            preview.Reports,
            overlap,
            snapshotTimestampUtc,
            confirmationMode,
            confirmedAtUtc,
            confirmedByUserId,
            confirmationNote);
    }

    public static BillingPreparationHoursMultiPreview PreviewMany(
        IReadOnlyList<int> selectedSubContractIds,
        DateTime? fromDateInclusive,
        DateTime? toDateInclusive,
        IReadOnlyList<BillingHourlySubContractDraft> hourlySubContracts,
        IReadOnlyList<BillingHourReportFact> hourReports)
    {
        ArgumentNullException.ThrowIfNull(selectedSubContractIds);
        ArgumentNullException.ThrowIfNull(hourlySubContracts);
        ArgumentNullException.ThrowIfNull(hourReports);

        var selected = selectedSubContractIds.Where(id => id > 0).Distinct().ToList();
        if (selected.Count == 0)
            return BillingPreparationHoursMultiPreview.Empty();
        if (fromDateInclusive is not DateTime from || toDateInclusive is not DateTime to)
            return BillingPreparationHoursMultiPreview.Empty();
        if (to.Date < from.Date)
            return BillingPreparationHoursMultiPreview.Empty("עד תאריך חייב להיות באותו יום או אחרי מתאריך.");

        var perSub = new List<BillingPreparationHoursSubContractPreview>();
        foreach (var id in selected)
        {
            var match = hourlySubContracts.FirstOrDefault(s => s.MasterPlanSubContractId == id);
            var name = match?.Name ?? id.ToString();
            var preview = Preview(id, from, to, hourlySubContracts, hourReports);
            perSub.Add(new BillingPreparationHoursSubContractPreview(
                id,
                name,
                preview.ReportCount,
                preview.TotalHours,
                preview.Reports,
                preview.ValidationMessage));
        }

        var union = DistinctReports(perSub.SelectMany(p => p.Reports));
        var zeroNames = perSub
            .Where(p => p.ReportCount == 0 && p.ValidationMessage is null)
            .Select(p => p.Name)
            .ToList();
        var firstError = perSub.Select(p => p.ValidationMessage).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
        var validation = firstError
                         ?? (zeroNames.Count > 0 ? FormatZeroReportMessage(zeroNames) : null);
        return new BillingPreparationHoursMultiPreview(
            from.Date,
            to.Date,
            selected.Count,
            union.Count,
            union.Sum(r => r.Hours),
            union,
            perSub,
            zeroNames,
            validation);
    }

    public static IReadOnlyList<BillingPreparationHoursLineSnapshot> ComposeMany(
        IReadOnlyList<int> selectedSubContractIds,
        DateTime fromDateInclusive,
        DateTime toDateInclusive,
        IReadOnlyList<BillingHourlySubContractDraft> hourlySubContracts,
        IReadOnlyList<BillingHourReportFact> hourReports,
        IReadOnlyList<int> overlappingHourReportIds,
        DateTime snapshotTimestampUtc,
        BillingConfirmationMode confirmationMode = BillingConfirmationMode.None,
        DateTime? confirmedAtUtc = null,
        int? confirmedByUserId = null,
        string? confirmationNote = null)
    {
        ArgumentNullException.ThrowIfNull(selectedSubContractIds);
        var preview = PreviewMany(
            selectedSubContractIds,
            fromDateInclusive,
            toDateInclusive,
            hourlySubContracts,
            hourReports);
        if (preview.ZeroReportNames.Count > 0)
            throw new InvalidOperationException(FormatZeroReportMessage(preview.ZeroReportNames));
        if (preview.ValidationMessage is not null)
            throw new InvalidOperationException(preview.ValidationMessage);

        return selectedSubContractIds
            .Where(id => id > 0)
            .Distinct()
            .Select(id => Compose(
                id,
                fromDateInclusive,
                toDateInclusive,
                hourlySubContracts,
                hourReports,
                overlappingHourReportIds,
                snapshotTimestampUtc,
                confirmationMode,
                confirmedAtUtc,
                confirmedByUserId,
                confirmationNote))
            .ToList();
    }

    public static IReadOnlyList<IReadOnlyList<BillingPreparationHoursLineSnapshot>> GroupCompatibleScopes(
        IReadOnlyList<BillingPreparationHoursLineSnapshot> hours)
    {
        ArgumentNullException.ThrowIfNull(hours);
        if (hours.Count == 0)
            return [];

        return hours
            .GroupBy(h => (
                h.FromDate.Date,
                h.ToDate.Date,
                h.ConfirmationMode,
                h.ConfirmedAtUtc,
                h.ConfirmedByUserId,
                h.ConfirmationNote))
            .Select(g => (IReadOnlyList<BillingPreparationHoursLineSnapshot>)g
                .OrderBy(x => x.MasterPlanSubContractId)
                .ToList())
            .ToList();
    }

    public static string FormatZeroReportMessage(IReadOnlyList<string> subContractNames)
    {
        ArgumentNullException.ThrowIfNull(subContractNames);
        var lines = new List<string> { "לא נמצאו דיווחי שעות בטווח שנבחר עבור:" };
        foreach (var name in subContractNames.Where(n => !string.IsNullOrWhiteSpace(n)))
            lines.Add("• " + name.Trim());
        return string.Join(Environment.NewLine, lines);
    }

    public static bool ContainsForbiddenUnbilledClaim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.Contains("לא מחויבות", StringComparison.Ordinal)
               || text.Contains("שטרם חויבו", StringComparison.Ordinal)
               || text.Contains("unbilled", StringComparison.OrdinalIgnoreCase)
               || text.Contains("hours not yet billed", StringComparison.OrdinalIgnoreCase)
               || text.Contains("already billed", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<BillingPreparationHourReportSnapshot> DistinctReports(
        IEnumerable<BillingPreparationHourReportSnapshot> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        return reports
            .GroupBy(r => r.HoursReportId)
            .Select(g => g.First())
            .OrderBy(r => r.Date)
            .ThenBy(r => r.HoursReportId)
            .ToList();
    }
}

public readonly record struct BillingPreparationHoursScopePreview(
    IReadOnlyList<BillingPreparationHourReportSnapshot> Reports,
    int ReportCount,
    decimal TotalHours,
    string? ValidationMessage)
{
    public static BillingPreparationHoursScopePreview Empty(string? validationMessage = null) =>
        new([], 0, 0m, validationMessage);
}

public readonly record struct BillingPreparationHoursSubContractPreview(
    int MasterPlanSubContractId,
    string Name,
    int ReportCount,
    decimal TotalHours,
    IReadOnlyList<BillingPreparationHourReportSnapshot> Reports,
    string? ValidationMessage);

public readonly record struct BillingPreparationHoursMultiPreview(
    DateTime? FromDate,
    DateTime? ToDate,
    int SelectedSubContractCount,
    int ReportCount,
    decimal TotalHours,
    IReadOnlyList<BillingPreparationHourReportSnapshot> Reports,
    IReadOnlyList<BillingPreparationHoursSubContractPreview> PerSubContract,
    IReadOnlyList<string> ZeroReportNames,
    string? ValidationMessage)
{
    public static BillingPreparationHoursMultiPreview Empty(string? validationMessage = null) =>
        new(null, null, 0, 0, 0m, [], [], [], validationMessage);
}
