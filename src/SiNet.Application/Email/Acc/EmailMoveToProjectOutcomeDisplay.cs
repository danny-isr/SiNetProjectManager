using System.Text;

namespace SiNet.Application.Email.Acc;

/// <summary>
/// Maps MoveToProject outcomes to user-visible Hebrew status text.
/// Mirrors <see cref="EmailAccUploadOutcomeDisplay"/> — Outcome/Kind + Display, no parallel notification stack.
/// </summary>
public static class EmailMoveToProjectOutcomeDisplay
{
    public const int MaxFailureLines = 5;

    public static string Build(
        int movedCount,
        int totalCount,
        IReadOnlyList<EmailMoveToProjectAttachmentFailure>? failures,
        int alreadySameSourceCount = 0)
    {
        var failedCount = failures?.Count ?? 0;
        var completedCount = movedCount + alreadySameSourceCount;
        var allDone = failedCount == 0 && totalCount > 0 && completedCount >= totalCount;

        string summary;
        if (allDone)
        {
            summary = alreadySameSourceCount > 0 && movedCount == 0
                ? $"כל {totalCount} הקבצים כבר היו מתויקים לפרויקט."
                : $"תויקו {completedCount}/{totalCount} קבצים לפרויקט.";
        }
        else if (failedCount > 0)
        {
            summary = $"לא כל הקבצים הועברו: {completedCount}/{totalCount} ({failedCount} נכשלו).";
        }
        else
        {
            summary = $"לא כל הקבצים הועברו: {completedCount}/{totalCount}.";
        }

        var sb = new StringBuilder(summary);

        if (failures is { Count: > 0 })
        {
            var shown = Math.Min(failures.Count, MaxFailureLines);
            for (var i = 0; i < shown; i++)
            {
                var f = failures[i];
                sb.AppendLine();
                sb.Append("• ");
                sb.Append(string.IsNullOrWhiteSpace(f.FileName) ? $"קובץ #{f.InboxAttachmentId}" : f.FileName);
                sb.Append(" — ");
                sb.Append(ResolveKindHebrew(f.Kind, f.Detail));
            }

            var remaining = failures.Count - shown;
            if (remaining > 0)
            {
                sb.AppendLine();
                sb.Append("• ועוד ");
                sb.Append(remaining);
                sb.Append(" כשלונות.");
            }
        }

        if (!allDone)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("המשימה לא נסגרה. תקן את הקבצים שנכשלו (או בחר יעד אחר) ונסה שוב.");
        }

        return sb.ToString();
    }

    public static string ResolveKindHebrew(string kind, string? detail = null) =>
        kind switch
        {
            "Locked" => "הקובץ נעול לעריכה ב-ACC (כנראה כבר תויק בעבר).",
            "AlreadyMovedToProject" => "הקובץ כבר הועבר ליעד הנוכחי ב-ACC (אומת).",
            "AlreadyMovedConflict" => string.IsNullOrWhiteSpace(detail)
                ? "הקובץ כבר תויק ליעד אחר ב-ACC — לא ניתן להעביר שוב ליעד הנוכחי."
                : HumanizeDetail(detail),
            "FiledButMoveMetadataFailed" => string.IsNullOrWhiteSpace(detail)
                ? "הקובץ תויק פיזית, אך השלמת מטא-דאטת Move/Lock ב-ACC נכשלה."
                : HumanizeDetail(detail),
            "MissingInAcc" => "הקובץ חסר ב-ACC Inbox — נדרש שחזור/רענון.",
            "MissingAccItemId" => "חסר קישור ל-ACC Inbox (AccItemId) — יש להעלות/לשחזר ואז לנסות שוב.",
            "DownloadFailed" => "הורדת הקובץ מ-ACC נכשלה.",
            "NoFilingTag" => "לא נמצא תיוג יעד (ProjectFile) לתיוק.",
            "ZipFilingFailed" => string.IsNullOrWhiteSpace(detail)
                ? "תיוק תיקיית ZIP נכשל."
                : HumanizeDetail(detail),
            "FilingFailed" => string.IsNullOrWhiteSpace(detail)
                ? "תיוק הקובץ נכשל."
                : HumanizeDetail(detail),
            _ => string.IsNullOrWhiteSpace(detail)
                ? $"התיוק נכשל ({kind})."
                : HumanizeDetail(detail),
        };

    /// <summary>
    /// Turns known technical exception messages into short Hebrew user text; otherwise returns a trimmed detail.
    /// </summary>
    public static string HumanizeDetail(string detail)
    {
        if (detail.Contains("Google Drive", StringComparison.OrdinalIgnoreCase)
            && detail.Contains("not wired", StringComparison.OrdinalIgnoreCase))
        {
            return "יעד Google Drive אינו נתמך בתיוק (אין fallback).";
        }

        if (detail.Contains("Filing to Google Drive", StringComparison.OrdinalIgnoreCase))
        {
            return "יעד Google Drive אינו נתמך בתיוק (אין fallback).";
        }

        if (detail.Contains("ProjectAccMapping", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("Provision the ACC project mapping", StringComparison.OrdinalIgnoreCase))
        {
            return "חסר מיפוי ACC לפרויקט — יש להשלים את מיפוי הפרויקט ב-ACC לפני תיוק.";
        }

        // Strip noisy exception type prefixes if present: "NotSupportedException: ..."
        var colon = detail.IndexOf(':');
        if (colon > 0 && colon < 64 && detail.AsSpan(0, colon).Contains("Exception", StringComparison.Ordinal))
            detail = detail[(colon + 1)..].Trim();

        return detail.Length <= 180 ? detail : detail[..177] + "…";
    }
}
