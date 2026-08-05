namespace SiNet.Application.Diagnostics;

/// <summary>Hebrew labels and file-name slugs for <see cref="CrashReasonCategory"/>.</summary>
public static class CrashReasonCategoryDisplay
{
    public static string ToHebrew(CrashReasonCategory category) => category switch
    {
        CrashReasonCategory.Civil3DRepeatCrash => "קריסה חוזרת של Civil 3D",
        CrashReasonCategory.UnexpectedShutdown => "המחשב נכבה או אותחל מעצמו",
        CrashReasonCategory.BlueScreen => "מסך כחול",
        CrashReasonCategory.FreezeOrSlowness => "תקיעות ואיטיות",
        CrashReasonCategory.CrashDuringSpecificAction => "קריסה בפעולה מסוימת",
        CrashReasonCategory.Other => "אחר",
        _ => category.ToString(),
    };

    /// <summary>ASCII slug used inside report file names so the share folder is readable.</summary>
    public static string ToSlug(CrashReasonCategory category) => category switch
    {
        CrashReasonCategory.Civil3DRepeatCrash => "civil3d-repeat",
        CrashReasonCategory.UnexpectedShutdown => "unexpected-shutdown",
        CrashReasonCategory.BlueScreen => "blue-screen",
        CrashReasonCategory.FreezeOrSlowness => "freeze",
        CrashReasonCategory.CrashDuringSpecificAction => "specific-action",
        CrashReasonCategory.Other => "other",
        _ => "other",
    };

    public static string ToHebrew(CrashReportScope scope) => scope switch
    {
        CrashReportScope.Both => "אפליקציות ומכונה",
        CrashReportScope.ApplicationOnly => "אפליקציות בלבד",
        CrashReportScope.MachineOnly => "מכונה בלבד",
        _ => scope.ToString(),
    };

    public static string ToHebrew(CrashSeverity severity) => severity switch
    {
        CrashSeverity.Critical => "קריטי",
        CrashSeverity.AppCrash => "קריסת תוכנה",
        CrashSeverity.Supporting => "תומך",
        _ => severity.ToString(),
    };
}
