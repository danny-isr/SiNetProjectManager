using System.IO;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Task Workbench card display: TaskId, project number/name, zebra.</summary>
public sealed class TaskWorkbenchCardDisplayTests
{
    [Fact]
    public void TaskSummaryDto_includes_project_number_and_name()
    {
        var dto = SampleTask(
            taskId: 293,
            projectId: 136,
            projectNumber: "3214",
            projectName: "WF-REV-83-E2E-SI");

        Assert.Equal(293, dto.TaskId);
        Assert.Equal("3214", dto.ProjectNumber);
        Assert.Equal("WF-REV-83-E2E-SI", dto.ProjectName);
    }

    [Fact]
    public void Project_display_line_is_number_and_name_not_internal_id()
    {
        var dto = SampleTask(
            taskId: 293,
            projectId: 136,
            projectNumber: "3214",
            projectName: "WF-REV-83-E2E-SI");

        Assert.Equal("פרויקט 3214 — WF-REV-83-E2E-SI", dto.ProjectDisplayLine);
        Assert.DoesNotContain("136", dto.ProjectDisplayLine, StringComparison.Ordinal);
        Assert.Contains("ProjectId=136", dto.ProjectDisplayTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskId_display_and_tooltip_remain_present()
    {
        var dto = SampleTask(293, projectId: 136, projectNumber: "3214", projectName: "A");

        Assert.Equal("#293", dto.TaskIdDisplay);
        Assert.Equal("מספר משימה 293", dto.TaskIdTooltip);
    }

    [Fact]
    public void Null_project_remains_safe()
    {
        var dto = SampleTask(10, projectId: null, projectNumber: null, projectName: null);

        Assert.Null(dto.ProjectId);
        Assert.Null(dto.ProjectDisplayLine);
        Assert.Null(dto.ProjectDisplayTooltip);
        Assert.Equal("#10", dto.TaskIdDisplay);
    }

    [Fact]
    public void Project_number_formatting_matches_selector_authority()
    {
        Assert.Equal("3214", ProjectNumberFormatting.Format(3214f));
        Assert.Equal(string.Empty, ProjectNumberFormatting.Format(null));
    }

    [Fact]
    public void ShowTaskTypeName_false_when_duplicates_title()
    {
        var dto = SampleTask(1, 1, "1", "P") with
        {
            Title = "בדיקה / אישור פנימי",
            TaskTypeName = "בדיקה / אישור פנימי"
        };

        Assert.False(dto.ShowTaskTypeName);
    }

    [Fact]
    public void ShowTaskTypeName_true_when_distinct_from_title()
    {
        var dto = SampleTask(1, 1, "1", "P") with
        {
            Title = "תיוק חומר ראשוני",
            TaskTypeName = "FileInitialMaterials"
        };

        Assert.True(dto.ShowTaskTypeName);
    }

    [Fact]
    public void Task_card_xaml_binds_visible_TaskId_and_enables_zebra()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");

        Assert.Contains("Text=\"{Binding TaskIdDisplay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding TaskIdTooltip}\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Property=\"AutomationProperties.AutomationId\" Value=\"{Binding TaskId, StringFormat=Task.{0}}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ProjectDisplayLine}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Text=\"{Binding ProjectId, StringFormat=פרויקט {0}}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("AlternationCount\" Value=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsControl.AlternationIndex", xaml, StringComparison.Ordinal);
        Assert.Contains("SiSurfaceBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowTaskTypeName", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlTaskQueryService_batch_loads_project_display()
    {
        var sql = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Tasks/SqlTaskQueryService.cs");
        Assert.Contains("LoadProjectDisplayAsync", sql, StringComparison.Ordinal);
        Assert.Contains("ProjectNumberFormatting.Format", sql, StringComparison.Ordinal);
        Assert.Contains("projectIds.Contains(p.Id)", sql, StringComparison.Ordinal);
    }

    private static TaskSummaryDto SampleTask(
        int taskId,
        int? projectId,
        string? projectNumber,
        string? projectName) =>
        new(
            TaskId: taskId,
            ProjectId: projectId,
            TaskTypeCode: "T",
            TaskTypeName: "Type",
            StatusCode: "Open",
            StatusName: "Open",
            IsOpen: true,
            AssignedToUserId: 12,
            AssignedToUserName: "User 12",
            WorkQueueBucket: WorkQueueBucketCodes.Medium,
            WorkQueueBucketCode: WorkQueueBucketCodes.ToCode(WorkQueueBucketCodes.Medium),
            WorkQueueBucketDisplayName: WorkQueueBucketCodes.ToDisplayName(WorkQueueBucketCodes.Medium),
            WorkPriority: 6,
            DueDate: null,
            CreatedAt: new DateTime(2026, 9, 5, 13, 37, 0, DateTimeKind.Utc),
            LastTaskResultCode: null,
            Title: $"Task {taskId}",
            ComponentKey: null,
            ProjectNumber: projectNumber,
            ProjectName: projectName);

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
