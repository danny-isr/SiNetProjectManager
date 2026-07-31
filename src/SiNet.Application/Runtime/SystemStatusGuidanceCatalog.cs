namespace SiNet.Application.Runtime;

/// <summary>
/// Maps known subsystem status situations to Hebrew operator remediation text for «מצב מערכת».
/// </summary>
public static class SystemStatusGuidanceCatalog
{
    /// <summary>
    /// Returns guidance when the row is a known fixable problem; otherwise <see langword="null"/>.
    /// Prefer an existing <paramref name="existingGuidanceHe"/> when already set on the status.
    /// </summary>
    public static string? Resolve(
        string key,
        SubsystemRuntimeState state,
        string? summaryHe,
        string? existingGuidanceHe = null)
    {
        if (!string.IsNullOrWhiteSpace(existingGuidanceHe))
            return existingGuidanceHe.Trim();

        if (state is SubsystemRuntimeState.Idle or SubsystemRuntimeState.Running)
            return null;

        var summary = summaryHe ?? string.Empty;
        var keyNorm = key?.Trim() ?? string.Empty;

        if (string.Equals(keyNorm, "acc-service", StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyNorm, "acc", StringComparison.OrdinalIgnoreCase))
        {
            if (LooksLikeTlsFailure(summary)
                || state is SubsystemRuntimeState.Degraded or SubsystemRuntimeState.Stopped)
            {
                return AccServiceTlsGuidance;
            }

            if (summary.Contains("מקומי", StringComparison.Ordinal)
                || summary.Contains("Local", StringComparison.OrdinalIgnoreCase))
            {
                return AccLocalModeGuidance;
            }

            if (state == SubsystemRuntimeState.NotConfigured
                || summary.Contains("לא הוגדר", StringComparison.Ordinal))
            {
                return AccServiceNotConfiguredGuidance;
            }
        }

        if (string.Equals(keyNorm, "autodesk-acc", StringComparison.OrdinalIgnoreCase))
        {
            if (summary.Contains("2-legged", StringComparison.OrdinalIgnoreCase)
                || summary.Contains("Admin", StringComparison.Ordinal))
            {
                return AutodeskTwoLeggedOnlyGuidance;
            }

            if (state is SubsystemRuntimeState.Degraded or SubsystemRuntimeState.Stopped
                or SubsystemRuntimeState.NotConfigured)
            {
                return AutodeskTokenGeneralGuidance;
            }
        }

        if (string.Equals(keyNorm, "workflow-assignees", StringComparison.OrdinalIgnoreCase))
        {
            if (state is SubsystemRuntimeState.Degraded or SubsystemRuntimeState.NotConfigured
                || summary.Contains("assignee", StringComparison.OrdinalIgnoreCase)
                || summary.Contains("קבוצ", StringComparison.Ordinal))
            {
                return WorkflowAssigneesGuidance;
            }
        }

        if (string.Equals(keyNorm, "seed-baseline", StringComparison.OrdinalIgnoreCase))
        {
            if (state is SubsystemRuntimeState.Degraded or SubsystemRuntimeState.NotConfigured)
                return SeedBaselineGuidance;
        }

        if (string.Equals(keyNorm, "gmail", StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyNorm, "google", StringComparison.OrdinalIgnoreCase))
        {
            if (state is SubsystemRuntimeState.Stopped or SubsystemRuntimeState.NotConfigured
                or SubsystemRuntimeState.Degraded)
            {
                return GmailGuidance;
            }
        }

        return null;
    }

    /// <summary>Enrich a status row with catalog guidance when missing.</summary>
    public static SubsystemRuntimeStatus WithGuidance(SubsystemRuntimeStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var guidance = Resolve(status.Key, status.State, status.SummaryHe, status.GuidanceHe);
        if (string.Equals(guidance, status.GuidanceHe, StringComparison.Ordinal))
            return status;
        return status with { GuidanceHe = guidance };
    }

    private static bool LooksLikeTlsFailure(string summary) =>
        summary.Contains("SSL", StringComparison.OrdinalIgnoreCase)
        || summary.Contains("TLS", StringComparison.OrdinalIgnoreCase)
        || summary.Contains("certificate", StringComparison.OrdinalIgnoreCase)
        || summary.Contains("thumbprint", StringComparison.OrdinalIgnoreCase)
        || summary.Contains("לא זמין", StringComparison.Ordinal)
        || summary.Contains("Offline", StringComparison.OrdinalIgnoreCase)
        || summary.Contains("connection cannot be established", StringComparison.OrdinalIgnoreCase);

    internal const string AccServiceTlsGuidance =
        "חיבור TLS ל־AccService נכשל (נפוץ אחרי DB נקי שמוחק את ה־pins). "
        + "בהגדרות → ACC הזן BaseUrl (למשל https://localhost:8443), "
        + "הדבק את thumbprint התעודה בשדה Pinned Certificate Thumbprints "
        + "(מהודעת השגיאה presented thumbprint=… או מ־diag של AccService), "
        + "שמור והפעל מחדש את האפליקציה. ודא ש־SiOffice.AccService רץ.";

    internal const string AccServiceNotConfiguredGuidance =
        "כתובת AccService לא מוגדרת במסד (נפוץ אחרי DB נקי). "
        + "בהגדרות → ACC הזן BaseUrl ו־Pinned Certificate Thumbprints, שמור והפעל מחדש.";

    internal const string AccLocalModeGuidance =
        "המערכת במצב Local — אין חיבור Remote ל־AccService. "
        + "אם נדרש Remote: בהגדרות → ACC הזן BaseUrl (HTTPS) ו־thumbprint, שמור והפעל מחדש.";

    internal const string AutodeskTwoLeggedOnlyGuidance =
        "יש טוקן 2-legged בלבד — פעולות Admin ב־ACC דורשות התחברות משתמש (3-legged). "
        + "התחבר ל־Autodesk דרך מסכי ACC/הגדרות שמפעילים OAuth אינטראקטיבי, ואז רענן מצב מערכת.";

    internal const string AutodeskTokenGeneralGuidance =
        "בדיקת טוקן Autodesk נכשלה או לא הוגדרה. "
        + "ודא ש־Client ID/Secret של Autodesk ב־Vault או בהגדרות תקינים, ושיש חיבור לרשת Autodesk.";

    internal const string WorkflowAssigneesGuidance =
        "יש שלבי workflow בלי מוקצה. פתח «הקצאות משתמשים / קבוצות» (כפתור למעלה), "
        + "הוסף משתמשים פעילים לקבוצות OfficeManagement / SeniorManagement / Planners "
        + "(ולקבוצות Review לפי הצורך), והגדר ברירת מחדל כשיש יותר מחבר אחד. זה לא נפתר ב־Seed.";

    internal const string GmailGuidance =
        "Gmail לא מחובר. התחבר דרך מסכי המייל/הגדרות Google (שחזור שקט או התחברות אינטראקטיבית), "
        + "ודא ש־client secrets ו־token store מוגדרים, ואז רענן מצב מערכת.";

    internal const string SeedBaselineGuidance =
        "חסרים פריטי Seed בסיסיים (workflow / קבוצות / catalog / מיפוי סוג↔תהליך). "
        + "ב־DEBUG: כלי פיתוח → «טעינת Seed בסיסי». "
        + "לסוגי פרויקט בלי מיפוי: מנהלה → «מדיניות סוג↔תהליך». "
        + "אחרי התיקון רענן «מצב מערכת». הקצאות חברי קבוצה הן נפרדות (שורת workflow-assignees).";
}
