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
    }

    [Fact]
    public void Build_includes_per_file_hebrew_lines_for_known_kinds()
    {
        var failures = new[]
        {
            new EmailMoveToProjectAttachmentFailure(3, "נספחים.pdf", "FilingFailed",
                "NotSupportedException: Filing to Google Drive is not wired through ProjectFileFilingService. No fallback is performed."),
            new EmailMoveToProjectAttachmentFailure(4, "פוליסה.pdf", "Locked"),
        };

        var text = EmailMoveToProjectOutcomeDisplay.Build(0, 2, failures);

        Assert.StartsWith("תויקו 0/2 קבצים (2 נכשלו).", text);
        Assert.Contains("• נספחים.pdf — יעד Google Drive אינו נתמך בתיוק (אין fallback).", text);
        Assert.Contains("• פוליסה.pdf — הקובץ נעול לעריכה ב-ACC (כנראה כבר תויק בעבר).", text);
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
            text.Split('\n').Count(l => l.StartsWith('•') && !l.Contains("ועוד")));
    }

    [Theory]
    [InlineData("Locked", null, "הקובץ נעול לעריכה ב-ACC (כנראה כבר תויק בעבר).")]
    [InlineData("AlreadyMovedToProject", null, "הקובץ כבר הועבר לפרויקט לפי מטא-דאטה של ACC.")]
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
