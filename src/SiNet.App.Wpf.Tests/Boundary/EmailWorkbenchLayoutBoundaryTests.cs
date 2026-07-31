using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class EmailWorkbenchLayoutBoundaryTests
{
    [Fact]
    public void Email_workbench_project_context_bar_spans_full_width()
    {
        var surfaceXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailSurfaceView.xaml");
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        var projectBarIdx = surfaceXaml.IndexOf("ProjectSelectorView", StringComparison.Ordinal);
        // Row 0 = project bar, 1 = FollowQuote banner, 2 = filter bar, 3 = list+detail.
        var mainGridIdx = surfaceXaml.IndexOf("Grid Grid.Row=\"3\"", StringComparison.Ordinal);
        Assert.True(projectBarIdx >= 0);
        Assert.True(mainGridIdx > projectBarIdx);
        Assert.DoesNotContain("ProjectSelectorView", listXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_workbench_filter_bar_spans_full_width()
    {
        var surfaceXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailSurfaceView.xaml");
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("EmailListFilterBar", surfaceXaml, StringComparison.Ordinal);
        Assert.Contains("local:EmailListFilterBar Grid.Row=\"2\"", surfaceXaml, StringComparison.Ordinal);
        Assert.Contains("ApplyFiltersCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("LoadNextPageCommand", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_workbench_has_follow_quote_banner_actions()
    {
        var surfaceXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailSurfaceView.xaml");
        Assert.Contains("ShowFollowQuoteBanner", surfaceXaml, StringComparison.Ordinal);
        Assert.Contains("ClearFollowQuoteFilterCommand", surfaceXaml, StringComparison.Ordinal);
        Assert.Contains("OpenFollowQuoteProjectWorkCommand", surfaceXaml, StringComparison.Ordinal);
        Assert.Contains("תיוק קובץ בלי מייל", surfaceXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_panel_contains_only_email_list_not_project_selector()
    {
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        Assert.DoesNotContain("ProjectSelectorView", listXaml, StringComparison.Ordinal);
        Assert.Contains("EmailListItemCard", listXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_panel_contains_only_email_list_not_global_filters()
    {
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.DoesNotContain("SearchText", listXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadNextPageCommand", listXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectCommand", listXaml, StringComparison.Ordinal);
        Assert.Contains("SearchText", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_has_internal_scroll()
    {
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", listXaml, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer", listXaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"*\"", listXaml, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", listXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("VirtualizingStackPanel", listXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_can_scroll_inside_current_page()
    {
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");
        var listCodeBehind = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml.cs");

        Assert.Contains("CanContentScroll=\"False\"", listXaml, StringComparison.Ordinal);
        Assert.Contains("EmailListContainerStyle", listXaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.IsVirtualizing\" Value=\"False\"", listXaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"0\"", listXaml, StringComparison.Ordinal);
        Assert.Contains("EmailListScrollViewer", listXaml, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseWheel", listCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DisplayGroups", listXaml, StringComparison.Ordinal);
        Assert.Contains("Expander", listXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_50_uses_paging_not_scroll()
    {
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        Assert.Contains("LoadNextPageCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadNextPageCommand", listXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Previous_50_uses_paging_not_scroll()
    {
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        Assert.Contains("LoadPreviousPageCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadPreviousPageCommand", listXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void IsUnread_true_only_when_gmail_unread_label_present()
    {
        var gatewaySource = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");

        Assert.Contains("ResolveIsUnread", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("UNREAD", gatewaySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_unread_label_does_not_mark_unread()
    {
        var gatewaySource = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");

        Assert.Contains("labelIds is { Count: > 0 }", gatewaySource, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "EMAIL_LIST_MIGRATION.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
