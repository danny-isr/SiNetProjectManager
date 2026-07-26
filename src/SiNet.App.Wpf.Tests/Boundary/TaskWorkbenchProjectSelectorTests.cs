using System.IO;
using SiNet.App.Wpf.Surfaces.Tasks;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Layout guards for Task Workbench filter area (no global project selector).</summary>
public sealed class TaskWorkbenchProjectSelectorTests
{
    [Fact]
    public void Task_workbench_project_selector_is_not_in_action_toolbar()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var actionsSection = ExtractSection(xaml, "<!-- Actions toolbar -->", "<!-- Context / filter area -->");
        var titleSection = ExtractSection(xaml, "<!-- Title -->", "<!-- Actions toolbar -->");

        Assert.DoesNotContain("ProjectSelectorView", actionsSection, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectSelectorView", titleSection, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RefreshCommand}\"", actionsSection, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MoveDownCommand}\"", actionsSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_does_not_host_global_project_selector_in_filter_area()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var filterSection = ExtractSection(xaml, "<!-- Context / filter area -->", "<Border Grid.Row=\"3\"");

        Assert.DoesNotContain("DataContext=\"{Binding ProjectSelector}\"", filterSection, StringComparison.Ordinal);
        Assert.Contains("LocalProjectFilterSelector", filterSection, StringComparison.Ordinal);
        Assert.Contains("ProjectFilterDisplayText", filterSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_actions_toolbar_contains_only_actions()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var actionsSection = ExtractSection(xaml, "<!-- Actions toolbar -->", "<!-- Context / filter area -->");

        Assert.Contains("RefreshCommand", actionsSection, StringComparison.Ordinal);
        Assert.Contains("AddTaskCommand", actionsSection, StringComparison.Ordinal);
        Assert.Contains("DeleteTaskCommand", actionsSection, StringComparison.Ordinal);
        Assert.Contains("RepairQueueCommand", actionsSection, StringComparison.Ordinal);
        Assert.Contains("MoveUpCommand", actionsSection, StringComparison.Ordinal);
        Assert.Contains("MoveDownCommand", actionsSection, StringComparison.Ordinal);
        Assert.DoesNotContain("AvailableScopes", actionsSection, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedUserId", actionsSection, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectSelector", actionsSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_dialog_uses_existing_project_selector()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskCreateDialogView.xaml");
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskCreateDialogViewModel.cs");

        Assert.Contains("ProjectSelectorView", xaml, StringComparison.Ordinal);
        Assert.Contains("ProjectSelectorViewModel", vmSource, StringComparison.Ordinal);
        Assert.Contains("InMemoryCurrentProjectContext", vmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Priority_legacy_field_is_not_used_as_queue_position()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchViewModel.cs");
        var dialogSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskCreateDialogViewModel.cs");
        Assert.DoesNotContain("WorkPriority =", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkPriority =", dialogSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkPriority_is_displayed_as_queue_position()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        Assert.Contains("מיקום בתור", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding WorkPriority}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TaskCardTemplate", xaml, StringComparison.Ordinal);
    }

    private static string ExtractSection(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
