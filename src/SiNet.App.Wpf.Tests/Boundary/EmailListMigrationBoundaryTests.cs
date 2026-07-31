using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email;
using Xunit;
using SiNet.App.Wpf.Tests.Surfaces.Email;

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
        // The window hosts EmailSurfaceView, which in turn hosts EmailListView.
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");
        var surfaceXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailSurfaceView.xaml");
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("EmailSurfaceView", xaml, StringComparison.Ordinal);
        Assert.Contains("EmailListView", surfaceXaml, StringComparison.Ordinal);
        Assert.Contains("Binding EmailList", surfaceXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding DisplayGroups}\"", listXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding FlatDisplayEmails}\"", listXaml, StringComparison.Ordinal);
        Assert.Contains("EmailListItemCard", listXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GridView", listXaml, StringComparison.Ordinal);
        Assert.Contains("IEmailGateway", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new EmailManagementView", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyBridge", vmSource, StringComparison.Ordinal);
        Assert.Contains("EmailListViewModel", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_context_menu_wires_filing_and_status_commands()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");
        var composition = ReadRepoFile("src/SiNet.App.Composition/SiNetCompositionExtensions.cs");
        var v2Graph = ReadRepoFile("SiNetProjectManagerV2/Services/Composition/NewSystemServiceCollectionExtensions.cs");

        Assert.Contains("IEmailFilingService", listVmSource, StringComparison.Ordinal);
        Assert.Contains("IEmailStatusService", listVmSource, StringComparison.Ordinal);
        Assert.Contains("GetContextMenuDisabledReason", listVmSource, StringComparison.Ordinal);
        Assert.Contains("PlacementTarget.Tag.FileEmailToProjectCommand", listXaml, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ShowOnDisabled", listXaml, StringComparison.Ordinal);
        Assert.Contains("AncestorType=local:EmailListView", listXaml, StringComparison.Ordinal);
        Assert.Contains("📁 שייך לפרויקט", listXaml, StringComparison.Ordinal);
        Assert.Contains("↩️ בטל שיוך", listXaml, StringComparison.Ordinal);
        Assert.Contains("⏳ סמן כממתין לטיפול", listXaml, StringComparison.Ordinal);
        Assert.Contains("AddSiNetEmailWriteSql", composition, StringComparison.Ordinal);

        // The V2 graph reaches the email modules through AddSiNet(V2Hybrid), so the read/write ports
        // are asserted on the built graph instead of a literal call in the source.
        Assert.Contains("AddSiNet(SiNetHostMode.V2Hybrid", v2Graph, StringComparison.Ordinal);

        var services = new ServiceCollection();
        SiNetProjectManagerV2.Services.Composition.NewSystemServiceCollectionExtensions
            .AddSiNetNewSystemGraph(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IEmailFilingService));
        Assert.Contains(services, d => d.ServiceType == typeof(IEmailStatusService));
    }

    [Fact]
    public void Email_list_view_model_has_no_direct_db_write()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.DoesNotContain("SiNetDbContext", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlEmailFilingService", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GmailEmailModifyService", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_detail_action_bar_is_hosted_in_detail_component()
    {
        var detailXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailDetailView.xaml");
        var detailVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");

        Assert.Contains("EmailActionBarView", detailXaml, StringComparison.Ordinal);
        Assert.Contains("FileSelectedEmailAsync", detailVmSource, StringComparison.Ordinal);
        Assert.Contains("MoveSelectedEmailToProjectAsync", detailVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_filing_and_status_ports_registered_in_composition()
    {
        var doc = ReadRepoFile("docs/EMAIL_FILING_SERVICE_DESIGN.md");
        var appSource = ReadRepoFile("src/SiNet.Application/Email/IEmailFilingService.cs");
        var writeExtensions = ReadRepoFile("src/SiNet.Infrastructure.Sql/EmailWriteServiceCollectionExtensions.cs");
        var composition = ReadRepoFile("src/SiNet.App.Composition/SiNetCompositionExtensions.cs");

        Assert.Contains("IEmailFilingService", doc, StringComparison.Ordinal);
        Assert.Contains("interface IEmailFilingService", appSource, StringComparison.Ordinal);
        Assert.Contains("SqlEmailFilingService", writeExtensions, StringComparison.Ordinal);
        Assert.Contains("IEmailStatusService", writeExtensions, StringComparison.Ordinal);
        Assert.Contains("AddSiNetEmailWriteSql", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_supports_attachments_filter_and_count_display()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var composerSource = ReadRepoFile("src/SiNet.Application/Abstractions/Email/EmailMailboxQueryComposer.cs");

        Assert.Contains("AttachmentCount", cardXaml, StringComparison.Ordinal);
        Assert.Contains("ToggleAttachmentsOnlyCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("ToggleUnreadOnlyCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("ShowUnreadFilterActive", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("עם צרופות", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("AttachmentsOnly", listVmSource, StringComparison.Ordinal);
        Assert.Contains("UnreadOnly", listVmSource, StringComparison.Ordinal);
        Assert.Contains("has:attachment", composerSource, StringComparison.Ordinal);
        Assert.Contains("is:unread", composerSource, StringComparison.Ordinal);
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
        Assert.Contains("EmailWorkItemTaskFloatingHost", launcherSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_default_scope_is_inbox_not_all_mail()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();
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
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var gatewaySource = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");

        Assert.DoesNotContain("Migration", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyBridge", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Migration", gatewaySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_does_not_filter_by_project_by_default()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.Contains("ApplyProjectContextAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("BuildEmailListProjectContext", vmSource, StringComparison.Ordinal);
        Assert.Contains("EmailListDisplayMode.AllEmails", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_can_filter_linked_and_unlinked_emails()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("EmailProjectLinkFilter.Linked", listVmSource, StringComparison.Ordinal);
        Assert.Contains("EmailProjectLinkFilter.Unlinked", listVmSource, StringComparison.Ordinal);
        Assert.Contains("ApplyClientRowFilters", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_displays_labels_and_collapsible_label_groups()
    {
        var cardXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListItemCard.xaml");
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        Assert.Contains("VisibleLabelChips", cardXaml, StringComparison.Ordinal);
        Assert.Contains("BackgroundColor", cardXaml, StringComparison.Ordinal);
        Assert.Contains("HexToBrush", cardXaml, StringComparison.Ordinal);
        Assert.Contains("RowBackgroundColor", listXaml, StringComparison.Ordinal);
        Assert.Contains("LabelChips", listVmSource, StringComparison.Ordinal);
        Assert.Contains("HasAnyLabels", cardXaml, StringComparison.Ordinal);
        Assert.Contains("PrimaryLabel", listVmSource, StringComparison.Ordinal);
        Assert.Contains("EmailLabelGroupViewModel", listVmSource, StringComparison.Ordinal);
        Assert.Contains("DisplayGroups", listXaml, StringComparison.Ordinal);
        Assert.Contains("FlatDisplayEmails", listXaml, StringComparison.Ordinal);
        Assert.Contains("EmailLabelGroupHeader", listXaml, StringComparison.Ordinal);
        Assert.Contains("HasActiveProject", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_label_group_context_menu_contains_load_all()
    {
        var headerXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailLabelGroupHeader.xaml");

        Assert.Contains("LoadAllForLabelCommand", headerXaml, StringComparison.Ordinal);
        Assert.Contains("טען את כל המיילים מהלייבל הזה", headerXaml, StringComparison.Ordinal);
        Assert.Contains("LoadMoreForLabelCommand", headerXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_label_email_can_appear_in_multiple_label_groups_documented()
    {
        var doc = ReadRepoFile("docs/EMAIL_LIST_MIGRATION.md");

        Assert.Contains("more than one group", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LabelId", doc, StringComparison.Ordinal);
        Assert.Contains("1000", doc, StringComparison.Ordinal);
        Assert.Contains("dedupe", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Email_list_search_filters_by_subject_and_address()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("SubjectFilter", listVmSource, StringComparison.Ordinal);
        Assert.Contains("AddressFilter", listVmSource, StringComparison.Ordinal);
        Assert.Contains("SubjectFilter", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("AddressFilter", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_paging_uses_page_size_50_with_next_and_previous()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.Contains("EmailMailboxQuery.DefaultPageSize", listVmSource, StringComparison.Ordinal);
        Assert.Contains("PageTokenStack", listVmSource, StringComparison.Ordinal);
        Assert.Contains("LoadNextPageCommand", filterBarXaml, StringComparison.Ordinal);
        Assert.Contains("LoadPreviousPageCommand", filterBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_filters_reset_to_all_emails()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

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
        var surfaceXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailSurfaceView.xaml");
        var filterBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListFilterBar.xaml");

        Assert.DoesNotContain("ProjectSelectorView", listXaml, StringComparison.Ordinal);
        Assert.Contains("EmailListFilterBar", surfaceXaml, StringComparison.Ordinal);
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
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("DisconnectCommand", listVmSource, StringComparison.Ordinal);
        Assert.Contains("AuthService.LogoutAsync()", listVmSource, StringComparison.Ordinal);
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
        var listVmSource = EmailListImplementationSource.ReadCombined();
        var listXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailListView.xaml");

        Assert.Contains("PartialFailure", listVmSource, StringComparison.Ordinal);
        Assert.Contains("LoadWarning", listXaml, StringComparison.Ordinal);
        Assert.Contains("שיוך", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_does_not_hide_gmail_results_when_db_enrichment_fails()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("enrichmentWarning", listVmSource, StringComparison.Ordinal);
        Assert.Contains("EmailListLoadState.PartialFailure", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_viewmodel_does_not_write_to_db_directly()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.DoesNotContain("SiNetSQLDbContext", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteUpdateAsync", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChangesAsync", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_list_viewmodel_does_not_use_legacy_bridge()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.DoesNotContain("LegacyBridge", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_row_actions_use_row_busy_guard_without_parallel_gmail_writes()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("ExecuteRowActionAsync", listVmSource, StringComparison.Ordinal);
        Assert.Contains("_busyRowIds", listVmSource, StringComparison.Ordinal);
        Assert.Contains("_filingService", listVmSource, StringComparison.Ordinal);
        Assert.Contains("_statusService", listVmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_row_actions_use_apply_local_email_mutation()
    {
        var listVmSource = EmailListImplementationSource.ReadCombined();

        Assert.Contains("ApplyLocalEmailMutation", listVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyClientFilterAfterRowUpdate", listVmSource, StringComparison.Ordinal);
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
