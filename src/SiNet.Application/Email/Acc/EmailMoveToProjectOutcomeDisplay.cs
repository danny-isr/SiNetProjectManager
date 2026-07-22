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
        IReadOnlyList<EmailMoveToProjectAttachmentFailure>? failures)
    {
        var failedCount = failures?.Count ?? 0;
        var summary = failedCount == 0
            ? $"תויקו {movedCount}/{totalCount} קבצים לפרויקט."
            : $"תויקו {movedCount}/{totalCount} קבצים ({failedCount} נכשלו).";

        if (failures is null || failures.Count == 0)
            return summary;

        var sb = new StringBuilder(summary);
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

        return sb.ToString();
    }

    public static string ResolveKindHebrew(string kind, string? detail = null) =>
        kind switch
        {
            "Locked" => "הקובץ נעול לעריכה ב-ACC (כנראה כבר תויק בעבר).",
            "AlreadyMovedToProject" => "הקובץ כבר הועבר לפרויקט לפי מטא-דאטה של ACC.",
            "MissingInAcc" => "הקובץ חסר ב-ACC Inbox — נדרש שחזור/רענון.",
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

        // Strip noisy exception type prefixes if present: "NotSupportedException: ..."
        var colon = detail.IndexOf(':');
        if (colon > 0 && colon < 64 && detail.AsSpan(0, colon).Contains("Exception", StringComparison.Ordinal))
            detail = detail[(colon + 1)..].Trim();

        return detail.Length <= 180 ? detail : detail[..177] + "…";
    }
}
