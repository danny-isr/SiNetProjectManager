namespace SiNet.Application.Inspection;

/// <summary>
/// Shared rules for building the inspection questionnaire tree (parity with legacy FloatingInspectionView).
/// </summary>
public static class InspectionQuestionnaireRules
{
    /// <summary>Known labels that support automatic fill from project/report data.</summary>
    public static readonly HashSet<string> AutoFieldLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "שם פרויקט", "מספר פרויקט",
        "ישוב", "רשות מקומית",
        "תאריך", "ממלא דוח", "כתובת מייל",
        "מספר דוח",
        "Today", "User", "Email",
    };

    public const string ManualStatus = "Manual";
    public const string NotApplicable = "NotApplicable";
    public const string Failed = "Failed";
    public const string ManagerReview = "ManagerReview";

    /// <summary>Numbered sub-notes use a 3-level index (e.g. 1.1.1) — at least two dots.</summary>
    public static bool IsNumberedSubNote(string? noteSubIndex) =>
        !string.IsNullOrEmpty(noteSubIndex) && noteSubIndex.Count(c => c == '.') >= 2;

    /// <summary>General Chapter-0 fields are base notes (no dots in SubIndex).</summary>
    public static bool IsGeneralBaseNote(string? noteSubIndex) =>
        string.IsNullOrEmpty(noteSubIndex) || !noteSubIndex.Contains('.');

    /// <summary>
    /// Regular note validation: missing status, or status ≠ N/A with empty text.
    /// General fields are never validated by this rule.
    /// </summary>
    public static bool HasValidationError(string? status, string? text) =>
        string.IsNullOrWhiteSpace(status)
        || (!string.Equals(status, NotApplicable, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(text));

    /// <summary>Export is blocked when any numbered note fails validation or is ManagerReview.</summary>
    public static bool CanExportNotes(IEnumerable<(string? Status, string? Text)> notes)
    {
        foreach (var (status, text) in notes)
        {
            if (string.Equals(status, ManagerReview, StringComparison.Ordinal))
                return false;
            if (HasValidationError(status, text))
                return false;
        }

        return true;
    }

    /// <summary>Legacy auto-sync: typing text sets Failed; clearing text clears status.</summary>
    public static string? SyncStatusAfterTextChange(string? currentStatus, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (string.IsNullOrWhiteSpace(currentStatus)
                || string.Equals(currentStatus, NotApplicable, StringComparison.Ordinal))
            {
                return Failed;
            }

            return currentStatus;
        }

        return string.Empty;
    }
}
