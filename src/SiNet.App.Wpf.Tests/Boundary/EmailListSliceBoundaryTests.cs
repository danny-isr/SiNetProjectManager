using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Boundary guards for Email List slice: labels, unread, project context, project group mode.</summary>
public sealed class EmailListSliceBoundaryTests
{
    [Fact]
    public void Email_card_displays_labels()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");
        var rowSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowDesignData.cs");

        Assert.Contains("VisibleLabelChips", cardXaml, StringComparison.Ordinal);
        Assert.Contains("MaxVisibleLabelChips", rowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_card_hides_labels_area_when_no_labels()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");

        Assert.Contains("HasAnyLabels", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_card_limits_many_labels_compactly()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");
        var rowSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowDesignData.cs");

        Assert.Contains("ExtraLabelCount", cardXaml, StringComparison.Ordinal);
        Assert.Contains("HasExtraLabels", rowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Unread_email_has_clear_visual_marker()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");

        Assert.Contains("SiPrimaryBrush", cardXaml, StringComparison.Ordinal);
        Assert.Contains("IsUnread", cardXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("לא נקרא", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Unread_email_has_blue_side_bar()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");

        Assert.Contains("SiPrimaryBrush", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"True\"", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_email_has_gray_side_bar()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");

        Assert.Contains("SiSecondaryBrush", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_email_has_normal_visual_style()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");

        Assert.Contains("FontWeight" , cardXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"Normal\"", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Unread_email_does_not_rely_only_on_background_color()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");

        Assert.Contains("SiPrimaryBrush", cardXaml, StringComparison.Ordinal);
        Assert.Contains("SiSecondaryBrush", cardXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("לא נקרא", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_card_shows_attachment_icon_when_has_attachments()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");

        Assert.Contains("HasAttachments", cardXaml, StringComparison.Ordinal);
        Assert.Contains("&#x1F4CE;", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_card_shows_linked_project()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");

        Assert.Contains("LinkedProjectBadge", cardXaml, StringComparison.Ordinal);
        Assert.Contains("IsLinked", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_card_shows_unlinked_state()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");

        Assert.Contains("ProjectLinkDisplay", cardXaml, StringComparison.Ordinal);
        Assert.Contains("IsLinked", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"False\"", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_card_displays_project_link_state()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("ProjectLinkState", vmSource, StringComparison.Ordinal);
        Assert.Contains("ProjectNumber", vmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_card_displays_linked_project_number_and_name()
    {
        var rowSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowDesignData.cs");

        Assert.Contains("LinkedProjectBadge", rowSource, StringComparison.Ordinal);
        Assert.Contains("ProjectNumber", rowSource, StringComparison.Ordinal);
        Assert.Contains("ProjectName", rowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_workbench_hosts_project_selector_outside_email_list_component()
    {
        var windowXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        Assert.Contains("ProjectSelectorView", windowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectSelectorView", listXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_component_does_not_own_project_selector()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.DoesNotContain("ProjectSelectorViewModel", listVmSource, StringComparison.Ordinal);
        Assert.Contains("ApplyProjectContextAsync", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_receives_project_context_as_input()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var contextSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListProjectContext.cs");

        Assert.Contains("EmailListProjectContext", listVmSource, StringComparison.Ordinal);
        Assert.Contains("SelectedProjectContext", listVmSource, StringComparison.Ordinal);
        Assert.Contains("GroupHeaderDisplay", contextSource, StringComparison.Ordinal);
    }

    [Fact]
    public void No_duplicate_project_selector_created()
    {
        var windowXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        Assert.Equal(1, CountOccurrences(windowXaml, "ProjectSelectorView"));
        Assert.Equal(0, CountOccurrences(listXaml, "ProjectSelectorView"));
    }

    [Fact]
    public void No_project_selected_shows_all_emails_mode()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("EmailListDisplayMode.AllEmails", listVmSource, StringComparison.Ordinal);
        Assert.Contains("IsAllEmailsMode", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_project_shows_project_email_group()
    {
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("ShowProjectGroupChrome", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("ProjectGroupHeader", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("EmailListDisplayMode.ProjectEmails", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_project_group_loads_first_10_emails()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("ProjectEmailChunkSize = 10", listVmSource, StringComparison.Ordinal);
        Assert.Contains("GetProjectEmailsByProjectLabelAsync", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_project_group_show_more_loads_next_10()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("ShowMoreProjectEmails", listVmSource, StringComparison.Ordinal);
        Assert.Contains("ShowMoreProjectEmailsCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("ProjectEmailChunkSize", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void All_emails_mode_keeps_50_item_paging()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("EmailMailboxQuery.DefaultPageSize", listVmSource, StringComparison.Ordinal);
        Assert.Contains("LoadNextPageCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("ShowAllEmailsPaging", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_group_does_not_replace_gmail_50_paging_unintentionally()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("ShowAllEmailsPaging", listVmSource, StringComparison.Ordinal);
        Assert.Contains("IsAllEmailsMode", listVmSource, StringComparison.Ordinal);
        Assert.Contains("GetMailboxPageAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("GetProjectEmailsByProjectLabelAsync", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Disconnect_button_uses_existing_service_or_is_disabled()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("_authService.Logout()", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void No_Gmail_write_operations_in_this_slice()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var windowVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.DoesNotContain("IEmailFilingService", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SendAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("ShowDeferredWriteActions => false", windowVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void No_LegacyBridge_in_email_list_slice()
    {
        foreach (var path in new[]
        {
            "src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs",
        })
        {
            var source = ReadRepoFile(path);
            Assert.DoesNotContain("LegacyBridge", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new EmailManagementView", source, StringComparison.Ordinal);
        }

        var windowVm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        Assert.DoesNotContain("LegacyBridge", windowVm, StringComparison.Ordinal);
        Assert.DoesNotContain("new EmailManagementView", windowVm, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

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
            if (File.Exists(Path.Combine(dir.FullName, "SiNetProjectManager_GitHub.sln"))
                || File.Exists(Path.Combine(dir.FullName, "docs", "EMAIL_LIST_MIGRATION.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
