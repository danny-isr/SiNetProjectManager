using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class EmailListMigrationBoundaryTests
{
    [Fact]
    public void Email_list_migration_doc_exists()
    {
        var doc = ReadRepoFile("docs/EMAIL_LIST_MIGRATION.md");
        Assert.Contains("EmailListView", doc, StringComparison.Ordinal);
        Assert.Contains("IEmailGateway", doc, StringComparison.Ordinal);
        Assert.Contains("read-only", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Email_list_view_extracted_from_email_window()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("EmailListView", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding EmailList", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding EmailsView}\"", listXaml, StringComparison.Ordinal);
        Assert.Contains("IEmailGateway", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new EmailManagementView", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyBridge", vmSource, StringComparison.Ordinal);
        Assert.Contains("EmailListViewModel", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_does_not_write_gmail_labels_in_v1()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("ShowDeferredWriteActions => false", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IEmailFilingService", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_filing_port_design_exists_without_sql_implementation()
    {
        var doc = ReadRepoFile("docs/EMAIL_FILING_SERVICE_DESIGN.md");
        var appSource = ReadRepoFile("src/SiNet.Application/Email/IEmailFilingService.cs");
        var composition = ReadRepoFile("src/SiNet.App.Composition/SiNetCompositionExtensions.cs");

        Assert.Contains("IEmailFilingService", doc, StringComparison.Ordinal);
        Assert.Contains("write policy", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("interface IEmailFilingService", appSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IEmailFilingService", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_apply_context_and_work_surface_launcher_exist()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        var launcherSource = ReadRepoFile("src/SiNet.App.Wpf/WorkSurfaces/WorkSurfaceLauncher.cs");

        Assert.Contains("ApplyTaskContextAsync", vmSource, StringComparison.Ordinal);
        Assert.Contains("PrimaryWorkTargetEntityId", vmSource, StringComparison.Ordinal);
        Assert.Contains("IEmailInboxQueryService", vmSource, StringComparison.Ordinal);
        Assert.Contains("WorkSurfaceComponentKeys.IsEmailSurface", launcherSource, StringComparison.Ordinal);
        Assert.Contains("IEmailWindowFactory", launcherSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_default_loads_all_emails()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var gatewaySource = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");

        Assert.Contains("GetMailboxPageAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("CanLoadEmails() => !IsBusy && _isConnected()", listVmSource, StringComparison.Ordinal);
        Assert.Contains("DefaultMailboxQuery", gatewaySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_does_not_filter_by_project_by_default()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.Contains("FilterByCurrentProject", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CanLoadEmails() =>\n        !IsBusy && _currentProject", vmSource, StringComparison.Ordinal);
        Assert.Contains("OptionalProjectLabel = FilterByCurrentProject", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_can_filter_linked_and_unlinked_emails()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("EmailProjectLinkFilter.Linked", listVmSource, StringComparison.Ordinal);
        Assert.Contains("EmailProjectLinkFilter.Unlinked", listVmSource, StringComparison.Ordinal);
        Assert.Contains("ApplyClientLinkFilter", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_displays_labels_and_groups_by_primary_label()
    {
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("LabelsDisplay", listXaml, StringComparison.Ordinal);
        Assert.Contains("PrimaryLabel", listVmSource, StringComparison.Ordinal);
        Assert.Contains("PropertyGroupDescription(nameof(EmailListRow.PrimaryLabel))", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_search_filters_by_subject_and_address()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");

        Assert.Contains("SubjectFilter", listVmSource, StringComparison.Ordinal);
        Assert.Contains("AddressFilter", listVmSource, StringComparison.Ordinal);
        Assert.Contains("EmailList.SubjectFilter", xaml, StringComparison.Ordinal);
        Assert.Contains("EmailList.AddressFilter", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_paging_uses_page_size_50_with_next_and_previous()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");

        Assert.Contains("DefaultPageSize", listVmSource, StringComparison.Ordinal);
        Assert.Contains("_pageTokenStack", listVmSource, StringComparison.Ordinal);
        Assert.Contains("LoadNextPageCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("LoadPreviousPageCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_filters_reset_to_all_emails()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("ClearFiltersAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("SelectedProjectLinkFilter = EmailProjectLinkFilter.All", listVmSource, StringComparison.Ordinal);
        Assert.Contains("FilterByCurrentProject = false", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_does_not_use_legacy_email_window()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        Assert.DoesNotContain("new EmailManagementView", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyBridge", vmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_workflow_gaps_documented()
    {
        var doc = ReadRepoFile("docs/WORK_SURFACE_WORKFLOW_INTEGRATION.md");
        Assert.Contains("IEmailFilingService", doc, StringComparison.Ordinal);
        Assert.Contains("filing write", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Email_list_uses_thread_link_query_service()
    {
        var sqlSource = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/SqlEmailThreadLinkQueryService.cs");
        var extensions = ReadRepoFile("src/SiNet.Infrastructure.Sql/EmailReadServiceCollectionExtensions.cs");

        Assert.Contains("IEmailThreadLinkQueryService", extensions, StringComparison.Ordinal);
        Assert.Contains("ThreadStatusMapping", sqlSource, StringComparison.Ordinal);
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
