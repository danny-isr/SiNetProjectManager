using System.IO;
using SiNetSQL.Services.InspectionSync;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

public sealed class InspectionTemplateCreateContractTests
{
    [Fact]
    public void Tag_grammar_scan_with_zero_rows_yields_empty_sync_rows()
    {
        var result = InspectionTemplateTagGrammar.ScanAndBuild([]);
        Assert.Empty(result.SyncRows);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Tag_grammar_builds_numbered_and_general_rows_from_tags()
    {
        var tags = new List<TemplateScanTag>
        {
            new()
            {
                SectionCode = "1.1",
                Title = "פרק א",
                DefaultText = "ברירת מחדל",
                IsStatusTag = true,
                Row = 0,
                Col = 2,
            },
            new()
            {
                SectionCode = string.Empty,
                GeneralTagLabel = "שם פרויקט",
                IsGeneralTag = true,
                Row = 1,
                Col = 0,
            },
            new()
            {
                SectionCode = string.Empty,
                GeneralTagLabel = TemplateTagValidator.PlannerResponseTagLabel,
                IsGeneralTag = true,
                IsPlannerResponseColumnTag = true,
                Row = 2,
                Col = 5,
            },
        };

        var rows = InspectionTemplateTagGrammar.BuildSyncRowsFromTags(tags);
        Assert.Contains(rows, r => r.SectionCode == "1.1" && r.ChapterNumber == 1);
        Assert.Contains(rows, r => r.ChapterNumber == 0 && r.SectionCode == "שם פרויקט");
        Assert.DoesNotContain(rows, r => r.SectionCode == TemplateTagValidator.PlannerResponseTagLabel);
    }

    [Fact]
    public void Validator_requires_planner_response_tag()
    {
        var errors = TemplateTagValidator.Validate([]);
        Assert.Contains(errors, e => e.RuleCode == "MISSING_PLANNER_RESPONSE_TAG");
    }

    [Fact]
    public void Scan_sheet_cells_with_valid_paired_tags_produces_sync_rows()
    {
        IList<IList<object>> cells =
        [
            ["header"],
            [$"<<1.1 כותרת [טקסט]>>", "<<1.1 $>>", $"<<{TemplateTagValidator.PlannerResponseTagLabel}>>"],
        ];

        var scan = InspectionTemplateTagGrammar.ScanAndBuild(cells);
        Assert.False(scan.HasErrors);
        Assert.NotEmpty(scan.SyncRows);
        Assert.Contains(scan.SyncRows, r => r.SectionCode == "1.1");
    }

    [Fact]
    public void ViewModel_create_requires_selected_template_not_empty_fallback()
    {
        var vmPath = Path.Combine(
            RepoRoot,
            "src",
            "SiNet.App.Wpf",
            "Surfaces",
            "Inspection",
            "InspectionWindowViewModel.cs");
        var text = File.ReadAllText(vmPath);
        Assert.DoesNotContain("native://empty-template", text, StringComparison.Ordinal);
        Assert.Contains("EnsureSeries", text, StringComparison.Ordinal);
        Assert.Contains("יש לבחור תבנית Google תקינה", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_create_service_fail_closed_message_is_documented()
    {
        var path = Path.Combine(
            RepoRoot,
            "src",
            "SiNet.Infrastructure.Sql",
            "Services",
            "Inspection",
            "SqlInspectionCommandServices.cs");
        var text = File.ReadAllText(path);
        Assert.Contains("לא נמצאו סעיפים תקינים בתבנית ולכן הדוח לא נוצר.", text, StringComparison.Ordinal);
        Assert.Contains("HydrateEmptyReportFromTemplateAsync", text, StringComparison.Ordinal);
        Assert.Contains("EnsureSeriesAsync", text, StringComparison.Ordinal);
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
