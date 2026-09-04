namespace SiNet.Application.Identity;

/// <summary>Hebrew footer / tooltip text for identity coherence.</summary>
public static class IdentityStatusDisplay
{
    public const string PendingMessage =
        "המשתמש זוהה ונוצר במערכת,\n" +
        "אך טרם הוגדרו עבורו הרשאות.\n" +
        "יש לפנות למנהל המערכת לצורך השלמת פרטי המשתמש וההרשאות.";

    public const string PendingStatusLine = "סטטוס: ממתין לאישור מנהל מערכת";

    public static string FormatFooter(IdentityCoherenceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.Status switch
        {
            IdentityCoherenceStatus.Checking => "זהות: בודק…",
            IdentityCoherenceStatus.PendingApproval => "זהות: ממתין לאישור מנהל מערכת",
            IdentityCoherenceStatus.Match =>
                $"זהות: תקינה | {snapshot.SiUserName ?? "?"} | {snapshot.SiUserEmail ?? "?"}",
            IdentityCoherenceStatus.AccUnverified =>
                FormatAccUnverified(snapshot),
            IdentityCoherenceStatus.IncompleteSiUser =>
                $"זהות: חסרה כתובת במערכת | {snapshot.SiUserName ?? "?"}",
            IdentityCoherenceStatus.NotConnected =>
                $"זהות: Google לא מחובר | מערכת: {snapshot.SiUserEmail ?? "(ריק)"}",
            IdentityCoherenceStatus.Mismatch when snapshot.GoogleMatch == false =>
                $"⚠ אי התאמת משתמש | מערכת: {snapshot.SiUserEmail} | Google: {snapshot.GoogleEmail}",
            IdentityCoherenceStatus.Mismatch when snapshot.AccMembershipMatch == false =>
                "⚠ משתמש המערכת אינו תואם לחברות בפרויקט ACC",
            IdentityCoherenceStatus.Mismatch =>
                $"⚠ אי התאמת זהות | {snapshot.FailureReason ?? "Mismatch"}",
            IdentityCoherenceStatus.Blocked => "זהות: חסום",
            _ => "זהות: לא ידוע",
        };
    }

    private static string FormatAccUnverified(IdentityCoherenceSnapshot snapshot)
    {
        var google = snapshot.GoogleMatch == true ? "Google: תקין" : "Google: לא מאומת";
        var acc = snapshot.AccRelevant
            ? "ACC: טרם אומת"
            : "ACC: לא רלוונטי / אין פרויקט פעיל";
        return $"זהות: חלקית | {google} | {acc} | {snapshot.SiUserEmail ?? "?"}";
    }

    public static string FormatGoogleMismatchDialog(string? siUserEmail, string? googleEmail) =>
        "אין התאמה בין חשבון המערכת לחשבון Google.\n" +
        $"החשבון המוגדר במערכת:\n{siUserEmail ?? "(ריק)"}\n\n" +
        $"החשבון שהיה מחובר ל-Google:\n{googleEmail ?? "(לא ידוע)"}\n\n" +
        "החשבון נותק.\n" +
        "יש להתחבר באמצעות החשבון המוגדר במערכת.";

    public static string FormatDetailsTooltip(IdentityCoherenceSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var accLine = s.AccRelevant
            ? $"ACC membership: {s.AccMembershipEmail} match={FormatBool(s.AccMembershipMatch)} access={s.AccAccessLevel ?? "n/a"}"
            : "ACC membership: לא רלוונטי / אין פרויקט פעיל";
        return
            $"SIUser.Id: {s.SiUserId}\n" +
            $"LoginName: {s.SiUserLoginName}\n" +
            $"Email: {s.SiUserEmail}\n" +
            $"Google: {s.GoogleEmail} match={FormatBool(s.GoogleMatch)}\n" +
            $"Gmail/Drive/Sheets: {FormatBool(s.GmailMatch)} (shared Google session)\n" +
            $"SiProjectId: {s.SiProjectId?.ToString() ?? "n/a"}\n" +
            $"AccProjectId: {s.AccProjectId ?? "n/a"}\n" +
            $"{accLine}\n" +
            $"ACC auth mode: {s.AccAuthMode}\n" +
            $"3-legged: {s.AutodeskThreeLeggedEmail} match={FormatBool(s.AutodeskThreeLeggedMatch)}\n" +
            $"Status: {s.Status}\n" +
            (s.FailureReason is null ? "" : $"Reason: {s.FailureReason}");
    }

    private static string FormatBool(bool? value) => value switch
    {
        true => "PASS",
        false => "FAIL",
        null => "n/a",
    };
}
