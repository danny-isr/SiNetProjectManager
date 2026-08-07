using SiNet.Application.Email.Acc;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

public sealed class EmailMoveToProjectOutcomeDisplayTests
{
    [Fact]
    public void Build_success_without_failures_is_summary_only()
    {
        var text = EmailMoveToProjectOutcomeDisplay.Build(2, 2, []);
        Assert.Equal("תויקו 2/2 קבצים לפרויקט.", text);
        Assert.DoesNotContain("•", text);
        Assert.DoesNotContain("המשימה לא נסגרה", text);
    }

    [Fact]
    public void Build_partial_failure_lists_files_and_keeps_task_open_message()
    {
        var failures = new[]
        {
            new EmailMoveToProjectAttachmentFailure(3, "נספחים.pdf", "FilingFailed",
                "NotSupportedException: Filing to Google Drive is not wired through ProjectFileFilingService. No fallback is performed."),
            new EmailMoveToProjectAttachmentFailure(4, "פוליסה.pdf", "Locked"),
        };

        var text = EmailMoveToProjectOutcomeDisplay.Build(0, 2, failures);

        Assert.StartsWith("לא כל הקבצים הועברו: 0/2 (2 נכשלו).", text);
        Assert.Contains("• נספחים.pdf — יעד Google Drive אינו נתמך בתיוק (אין fallback).", text);
        Assert.Contains("• פוליסה.pdf — הקובץ נעול לעריכה ב-ACC (כנראה כבר תויק בעבר).", text);
        Assert.Contains("המשימה לא נסגרה", text);
    }

    [Fact]
    public void Build_truncates_extra_failure_lines()
    {
        var failures = Enumerable.Range(1, 7)
            .Select(i => new EmailMoveToProjectAttachmentFailure(i, $"f{i}.pdf", "MissingInAcc"))
            .ToList();

        var text = EmailMoveToProjectOutcomeDisplay.Build(0, 7, failures);

        Assert.Contains("• ועוד 2 כשלונות.", text);
        Assert.Equal(EmailMoveToProjectOutcomeDisplay.MaxFailureLines,
            text.Split('\n').Count(l => l.TrimStart().StartsWith('•') && !l.Contains("ועוד")));
    }

    [Fact]
    public void AllFilesTransferred_requires_full_count()
    {
        var partial = new EmailMoveToProjectCoordinatorResult(
            EmailMoveToProjectOutcome.Failed,
            "x",
            MovedCount: 1,
            FailedCount: 1,
            TotalCount: 2);
        Assert.False(partial.AllFilesTransferred);

        var full = new EmailMoveToProjectCoordinatorResult(
            EmailMoveToProjectOutcome.Succeeded,
            "x",
            MovedCount: 2,
            FailedCount: 0,
            TotalCount: 2);
        Assert.True(full.AllFilesTransferred);
    }

    [Theory]
    [InlineData("AlreadyMovedToProject", null, "הקובץ כבר הועבר ליעד הנוכחי ב-ACC (אומת).")]
    [InlineData("AlreadyMovedConflict", null, "הקובץ כבר תויק ליעד אחר ב-ACC — לא ניתן להעביר שוב ליעד הנוכחי.")]
    [InlineData("FiledButMoveMetadataFailed", null, "הקובץ תויק פיזית, אך השלמת מטא-דאטת Move/Lock ב-ACC נכשלה.")]
    [InlineData("MissingAccItemId", null, "חסר קישור ל-ACC Inbox (AccItemId) — יש להעלות/לשחזר ואז לנסות שוב.")]
    [InlineData("Locked", null, "הקובץ נעול לעריכה ב-ACC (כנראה כבר תויק בעבר).")]
    [InlineData("MissingInAcc", null, "הקובץ חסר ב-ACC Inbox — נדרש שחזור/רענון.")]
    [InlineData("DownloadFailed", null, "הורדת הקובץ מ-ACC נכשלה.")]
    [InlineData("NoFilingTag", null, "לא נמצא תיוג יעד (ProjectFile) לתיוק.")]
    public void ResolveKindHebrew_maps_known_kinds(string kind, string? detail, string expected)
    {
        Assert.Equal(expected, EmailMoveToProjectOutcomeDisplay.ResolveKindHebrew(kind, detail));
    }

    [Fact]
    public void HumanizeDetail_maps_google_drive_not_wired()
    {
        var he = EmailMoveToProjectOutcomeDisplay.HumanizeDetail(
            "NotSupportedException: Filing to Google Drive is not wired through ProjectFileFilingService. No fallback is performed.");
        Assert.Equal("יעד Google Drive אינו נתמך בתיוק (אין fallback).", he);
    }
}
