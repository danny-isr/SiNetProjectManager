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
        Assert.Contains("EmailListItemCard", listXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GridView", listXaml, StringComparison.Ordinal);
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
    public void Email_list_default_scope_is_inbox_not_all_mail()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var composerSource = ReadRepoFile("src/SiNet.Application/Abstractions/Email/EmailMailboxQueryComposer.cs");

        Assert.Contains("EmailMailboxScope.Inbox", listVmSource, StringComparison.Ordinal);
        Assert.Contains("category:primary", composerSource, StringComparison.Ordinal);
        Assert.Contains("GetMailboxUnreadCountAsync", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_email_inbox_query_behavior_documented()
    {
        var doc = ReadRepoFile("docs/EMAIL_LIST_MIGRATION.md");

        Assert.Contains("category:primary", doc, StringComparison.Ordinal);
        Assert.Contains("label:INBOX", doc, StringComparison.Ordinal);
        Assert.Contains("GetMailboxUnreadCountAsync", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void No_schema_change_or_migration_for_inbox_scope()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var gatewaySource = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");

        Assert.DoesNotContain("Migration", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyBridge", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Migration", gatewaySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_does_not_filter_by_project_by_default()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.Contains("ApplyProjectContextAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("BuildEmailListProjectContext", vmSource, StringComparison.Ordinal);
        Assert.Contains("EmailListDisplayMode.AllEmails", listVmSource, StringComparison.Ordinal);
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
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("VisibleLabelChips", cardXaml, StringComparison.Ordinal);
        Assert.Contains("HasAnyLabels", cardXaml, StringComparison.Ordinal);
        Assert.Contains("PrimaryLabel", listVmSource, StringComparison.Ordinal);
        Assert.Contains("PropertyGroupDescription(nameof(EmailListRow.PrimaryLabel))", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_search_filters_by_subject_and_address()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("SubjectFilter", listVmSource, StringComparison.Ordinal);
        Assert.Contains("AddressFilter", listVmSource, StringComparison.Ordinal);
        Assert.Contains("SubjectFilter", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("AddressFilter", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_paging_uses_page_size_50_with_next_and_previous()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("EmailMailboxQuery.DefaultPageSize", listVmSource, StringComparison.Ordinal);
        Assert.Contains("_pageTokenStack", listVmSource, StringComparison.Ordinal);
        Assert.Contains("LoadNextPageCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("LoadPreviousPageCommand", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_filters_reset_to_all_emails()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("ClearFiltersAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("SelectedProjectLinkFilter = EmailProjectLinkFilter.All", listVmSource, StringComparison.Ordinal);
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
        Assert.Contains("Email List V3", doc, StringComparison.Ordinal);
        Assert.Contains("Not wired", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_uses_thread_link_query_service()
    {
        var sqlSource = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/SqlEmailThreadLinkQueryService.cs");
        var extensions = ReadRepoFile("src/SiNet.Infrastructure.Sql/EmailReadServiceCollectionExtensions.cs");

        Assert.Contains("IEmailThreadLinkQueryService", extensions, StringComparison.Ordinal);
        Assert.Contains("ThreadStatusMapping", sqlSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_component_is_standalone()
    {
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");
        var windowXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.DoesNotContain("ProjectSelectorView", listXaml, StringComparison.Ordinal);
        Assert.Contains("EmailListFilterBar", windowXaml, StringComparison.Ordinal);
        Assert.Contains("AccountStatusDisplay", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("ConnectCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("DisconnectCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("ApplyFiltersCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("LoadNextPageCommand", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_displays_connected_gmail_account()
    {
        var authSource = ReadRepoFile("src/SiNet.Application/Common/IConnectorAuthService.cs");
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("ConnectedAccountEmail", authSource, StringComparison.Ordinal);
        Assert.Contains("AccountStatusDisplay", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_disconnect_uses_logout_on_auth_service()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("DisconnectCommand", listVmSource, StringComparison.Ordinal);
        Assert.Contains("_authService.Logout()", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_uses_outlook_style_cards_not_datagrid_rows()
    {
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        Assert.Contains("EmailListItemCard", listXaml, StringComparison.Ordinal);
        Assert.Contains("<ListBox", listXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GridView", listXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_shows_partial_enrichment_warning()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        Assert.Contains("PartialFailure", listVmSource, StringComparison.Ordinal);
        Assert.Contains("LoadWarning", listXaml, StringComparison.Ordinal);
        Assert.Contains("שיוך", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_does_not_hide_gmail_results_when_db_enrichment_fails()
    {
        var listVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListViewModel.cs");

        Assert.Contains("enrichmentWarning", listVmSource, StringComparison.Ordinal);
        Assert.Contains("EmailListLoadState.PartialFailure", listVmSource, StringComparison.Ordinal);
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
