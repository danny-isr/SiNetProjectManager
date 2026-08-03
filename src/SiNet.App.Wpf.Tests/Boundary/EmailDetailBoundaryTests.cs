using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Email.Detail;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class EmailDetailBoundaryTests
{
    [Fact]
    public void Email_detail_component_doc_exists()
    {
        var doc = ReadRepoFile("docs/EMAIL_DETAIL_COMPONENT.md");
        Assert.Contains("EmailDetailView", doc, StringComparison.Ordinal);
        Assert.Contains("IEmailBodyRenderer", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_detail_extracted_from_email_window_without_legacy_bridge()
    {
        var windowXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");
        var surfaceXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailSurfaceView.xaml");
        var detailVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        var detailFolder = Path.Combine(
            ResolveRepoRoot(),
            "src/SiNet.App.Wpf/Surfaces/Email/Detail");

        // Standalone window hosts EmailSurfaceView; detail lives inside the surface.
        Assert.Contains("EmailSurfaceView", windowXaml, StringComparison.Ordinal);
        Assert.Contains("EmailDetailView", surfaceXaml, StringComparison.Ordinal);
        Assert.Contains("Binding EmailDetail", surfaceXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Calendar", windowXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LegacyBridge", detailVmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.MVVM", detailVmSource, StringComparison.Ordinal);

        foreach (var file in Directory.EnumerateFiles(detailFolder, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var content = File.ReadAllText(file);
                Assert.DoesNotContain("LegacyBridge", content, StringComparison.Ordinal);
                Assert.DoesNotContain("SiNetSQL.MVVM", content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Email_detail_ports_registered_in_composition()
    {
        var composition = ReadRepoFile("src/SiNet.App.Composition/SiNetCompositionExtensions.cs");
        var v2Graph = ReadRepoFile("SiNetProjectManagerV2/Services/Composition/NewSystemServiceCollectionExtensions.cs");
        var extensions = ReadRepoFile("src/SiNet.Infrastructure.Sql/EmailDetailServiceCollectionExtensions.cs");

        Assert.Contains("AddSiNetEmailDetailSql", composition, StringComparison.Ordinal);
        Assert.Contains("IEmailMoveToProjectService", extensions, StringComparison.Ordinal);
        Assert.Contains("IEmailAccIngestionService", extensions, StringComparison.Ordinal);

        // The V2 graph reaches the module through AddSiNet(V2Hybrid), so assert the resulting
        // registrations instead of a literal call in the source.
        Assert.Contains("AddSiNet(SiNetHostMode.V2Hybrid", v2Graph, StringComparison.Ordinal);

        var services = new ServiceCollection();
        SiNetProjectManagerV2.Services.Composition.NewSystemServiceCollectionExtensions
            .AddSiNetNewSystemGraph(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IEmailMoveToProjectService));
        Assert.Contains(services, d => d.ServiceType == typeof(IEmailAccIngestionService));
    }

    [Fact]
    public void Email_window_shell_delegates_selection_to_email_detail()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");
        Assert.Contains("EmailDetailViewModel", vmSource, StringComparison.Ordinal);
        Assert.Contains("EmailDetail.ApplySelectionAsync", vmSource, StringComparison.Ordinal);
        Assert.Contains("EmailDetailSelectionCoordinator", ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailDetailSelectionCoordinator.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Email_context_and_actions_sit_in_bottom_row_not_side_panel()
    {
        var detailXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailDetailView.xaml");
        var paneXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailWorkflowActionsPaneView.xaml");

        Assert.Contains("EmailWorkflowActionsPaneView Grid.Row=\"3\"", detailXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.Column=\"2\"", detailXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"280\"", detailXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Cursor=\"SizeWE\"", detailXaml, StringComparison.Ordinal);
        Assert.Contains("WrapPanel", paneXaml, StringComparison.Ordinal);
        Assert.Contains("פעולה:", paneXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_pane_has_no_idle_placeholder_that_flickers()
    {
        var paneXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailWorkflowActionsPaneView.xaml");
        Assert.DoesNotContain("בחר מייל לניתוח הקשר", paneXaml, StringComparison.Ordinal);
        Assert.Contains("מנתח הקשר...", paneXaml, StringComparison.Ordinal);
        Assert.Contains("IsLoading", paneXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Filing_uses_project_picker_without_setting_current_project()
    {
        var detailVm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        var host = ReadRepoFile("src/SiNet.Application/Email/Detail/IEmailFilingProjectPickerHost.cs");
        var v2Host = ReadRepoFile("SiNetProjectManagerV2/Services/Email/EmailFilingProjectPickerHost.cs");
        var app = ReadRepoFile("SiNetProjectManagerV2/App.xaml.cs");

        Assert.Contains("IEmailFilingProjectPickerHost", host, StringComparison.Ordinal);
        Assert.Contains("PickProjectAsync", host, StringComparison.Ordinal);
        Assert.Contains("SetCurrentProjectAsync", host, StringComparison.Ordinal); // documented must-not
        Assert.Contains("ProjectSelectorDialog", v2Host, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCurrentProjectAsync", v2Host, StringComparison.Ordinal);
        Assert.Contains("IEmailFilingProjectPickerHost", app, StringComparison.Ordinal);
        Assert.Contains("CanAttemptFileEmailToProject", detailVm, StringComparison.Ordinal);
        Assert.Contains("_filingProjectPicker.PickProjectAsync", detailVm, StringComparison.Ordinal);
        Assert.Contains("RefreshWorkflowContextAsync", detailVm, StringComparison.Ordinal);
        Assert.Contains("OverrideProjectId: null", detailVm, StringComparison.Ordinal);
    }

    [Fact]
    public void External_download_links_are_shown_for_click()
    {
        var stripXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailAttachmentStripView.xaml");
        var stripVm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailAttachmentStripViewModel.cs");
        var handler = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailExternalDownloadHandler.cs");

        Assert.DoesNotContain("פתח קישור הורדה", stripXaml, StringComparison.Ordinal);
        Assert.Contains("ExternalDownloadLinks", stripXaml, StringComparison.Ordinal);
        Assert.Contains("SetExternalDownloadLinks", stripVm, StringComparison.Ordinal);
        Assert.Contains("OpenDownloadLink", handler, StringComparison.Ordinal);
    }

    /// <summary>DEV-001 — see docs/DEV_BUG_EMAIL_LINK_EXTERNAL_WINDOW.md.</summary>
    [Fact]
    public void Body_links_leave_the_email_pane_through_the_chip_path()
    {
        var renderer = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/WebView2EmailBodyRenderer.cs");
        var detailVm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");

        Assert.Contains("NavigationStarting", renderer, StringComparison.Ordinal);
        Assert.Contains("NewWindowRequested", renderer, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true", renderer, StringComparison.Ordinal);
        Assert.Contains("ExternalLinkRequested", renderer, StringComparison.Ordinal);

        // Detector match must reuse OpenExternalDownloadLink — no second downloader.
        Assert.Contains("new EmailViewerPaneViewModel(OpenBodyLink)", detailVm, StringComparison.Ordinal);
        Assert.Contains("EmailExternalDownloadLinkDetector.IsExternalDownloadUrl(url)", detailVm, StringComparison.Ordinal);
    }

    /// <summary>DEV-004 / DEV-005 — see docs/DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md.</summary>
    [Fact]
    public void Action_bar_hands_reply_to_gmail_and_marks_read_through_the_modify_port()
    {
        var actionBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailActionBarView.xaml");
        var actionBarVm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailActionBarViewModel.cs");
        var detailVm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        var modifyPort = ReadRepoFile("src/SiNet.Application/Abstractions/Email/IEmailGmailModifyService.cs");
        var urlBuilder = ReadRepoFile("src/SiNet.Application/Email/GmailMessageUrlBuilder.cs");

        Assert.Contains("פתח ב-Gmail", actionBarXaml, StringComparison.Ordinal);
        Assert.Contains("סמן כנקרא", actionBarXaml, StringComparison.Ordinal);
        Assert.Contains("MarkAsReadEnabled", actionBarVm, StringComparison.Ordinal);
        Assert.Contains("OpenInGmailCommand", actionBarVm, StringComparison.Ordinal);
        Assert.Contains("GmailMessageUrlBuilder.Build", detailVm, StringComparison.Ordinal);
        Assert.Contains("TryMarkSelectedEmailAsReadAsync", detailVm, StringComparison.Ordinal);
        Assert.Contains("MarkAsReadAsync", modifyPort, StringComparison.Ordinal);
        Assert.Contains("#all/", urlBuilder, StringComparison.Ordinal);

        // Body rendering must still never host Gmail — only the action-bar browser launch may.
        var viewerXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailViewerPaneView.xaml");
        Assert.DoesNotContain("mail.google.com", viewerXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_association_ignores_global_project_override()
    {
        var sql = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Detail/SqlEmailWorkflowServices.cs");
        Assert.DoesNotContain("query.OverrideProjectId ?? message.ProjectId", sql, StringComparison.Ordinal);
        Assert.Contains("ResolveDefaultOfficeProjectIdAsync", sql, StringComparison.Ordinal);
        Assert.Contains("defaultOfficeProjectId", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Guard: mailbox "filed" is Gmail-label-only. See docs/EMAIL_ACC_SOURCE_OF_TRUTH.md
    /// and EmailSystemPrinciples §6.6 — do not reintroduce SQL ProjectId as IsFiledToProject.
    /// </summary>
    [Fact]
    public void Move_eligibility_uses_gmail_IsFiledToProject_not_sql_project_id()
    {
        var detailVm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        var sotDoc = ReadRepoFile("docs/EMAIL_ACC_SOURCE_OF_TRUTH.md");
        var principles = ReadRepoFile(
            "SiNetProjectManagerV2/Docs/Domains/Email/EmailSystemPrinciples-2026-05-26.md");

        Assert.Contains("_selectedEmail.IsFiledToProject", detailVm, StringComparison.Ordinal);
        Assert.Contains("Only Gmail project-label filing counts", detailVm, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEffectivelyFiled", detailVm, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "row.ProjectId == targetProjectId",
            detailVm,
            StringComparison.Ordinal);

        Assert.Contains("Gmail project label", sotDoc, StringComparison.Ordinal);
        Assert.Contains("IsEffectivelyFiled", sotDoc, StringComparison.Ordinal);
        Assert.Contains("### 6.6 Mailbox project association", principles, StringComparison.Ordinal);
    }

    /// <summary>
    /// Guard: tagging must resolve InboxMessageId for the selected Gmail message only.
    /// Blind PrimaryWorkTargetEntityId fallback patches sibling replies onto the SendQuote anchor.
    /// </summary>
    [Fact]
    public void Attachment_tagging_resolves_inbox_id_by_selected_message_identity()
    {
        var detailVm = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        var inboxQuery = ReadRepoFile("src/SiNet.Application/Email/IEmailInboxQueryService.cs");
        var sqlInbox = ReadRepoFile(
            "src/SiNet.Infrastructure.Sql/Services/Email/SqlEmailInboxQueryService.cs");
        var detailDoc = ReadRepoFile("docs/EMAIL_DETAIL_COMPONENT.md");

        Assert.Contains("FindByMessageIdentityAsync", inboxQuery, StringComparison.Ordinal);
        Assert.Contains("FindByMessageIdentityAsync", sqlInbox, StringComparison.Ordinal);
        Assert.Contains("ResolveInboxMessageIdForSelectedAsync", detailVm, StringComparison.Ordinal);
        Assert.Contains("IsPendingTaskTargetRow", detailVm, StringComparison.Ordinal);
        Assert.Contains("FindByMessageIdentityAsync", detailVm, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "? (_workSurfaceContext?.PrimaryWorkTargetEntityId is int primary && primary > 0",
            detailVm,
            StringComparison.Ordinal);
        Assert.Contains("InboxMessageId resolution for tagging", detailDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_tree_has_no_IsEffectivelyFiled_helper()
    {
        var root = ResolveRepoRoot();
        var hits = new List<string>();
        // Split so this test file does not match its own assertion string.
        var needle = "IsEffectively" + "Filed";
        foreach (var dirName in new[] { "src", "SiNetProjectManagerV2" })
        {
            var dir = Path.Combine(root, dirName);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.EndsWith("EmailDetailBoundaryTests.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (text.Contains(needle, StringComparison.Ordinal))
                {
                    hits.Add(Path.GetRelativePath(root, file));
                }
            }
        }

        Assert.True(hits.Count == 0, needle + " found in: " + string.Join(", ", hits));
    }

    /// <summary>
    /// Local body render: Gmail API Messages.Get(messageId) returns one message — no thread DOM.
    /// Body WebView2 must be Transient (per surface) and sit in a star-sized Grid row (not StackPanel).
    /// </summary>
    [Fact]
    public void Body_render_is_local_single_message_with_per_surface_webview()
    {
        var gateway = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");
        var viewerXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailViewerPaneView.xaml");
        var app = ReadRepoFile("SiNetProjectManagerV2/App.xaml.cs");
        var wpfDi = ReadRepoFile("src/SiNet.App.Wpf/NewSystemWpfServiceCollectionExtensions.cs");
        var renderer = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/WebView2EmailBodyRenderer.cs");

        Assert.Contains("Messages.Get(\"me\", messageId)", gateway, StringComparison.Ordinal);
        Assert.Contains("FormatEnum.Full", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("Users.Threads.Get", gateway, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"BodyHost\" Grid.Row=\"2\"", viewerXaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"*\"", viewerXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("mail.google.com", viewerXaml, StringComparison.Ordinal);
        // BodyHost must not live inside a StackPanel (WebView2 collapses to height 0).
        var bodyHostIndex = viewerXaml.IndexOf("x:Name=\"BodyHost\"", StringComparison.Ordinal);
        Assert.True(bodyHostIndex > 0);
        var beforeBodyHost = viewerXaml[..bodyHostIndex];
        var lastStackOpen = beforeBodyHost.LastIndexOf("<StackPanel", StringComparison.Ordinal);
        var lastStackClose = beforeBodyHost.LastIndexOf("</StackPanel>", StringComparison.Ordinal);
        Assert.True(
            lastStackOpen < 0 || lastStackClose > lastStackOpen,
            "BodyHost must not be nested inside an open StackPanel.");

        Assert.Contains(
            "AddTransient<IEmailBodyRenderer, WebView2EmailBodyRenderer>",
            wpfDi,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddTransient<SiNet.Application.Email.Detail.IEmailBodyRenderer",
            app,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddSingleton<SiNet.Application.Email.Detail.IEmailBodyRenderer",
            app,
            StringComparison.Ordinal);
        Assert.Contains("NavigateToString", renderer, StringComparison.Ordinal);
    }

    /// <summary>
    /// Embedded images (cid:) are served via virtual-host + WebResourceRequested, never inlined
    /// as Base64 data-URIs (crashes WebView2 with large images / hits NavigateToString size limit).
    /// </summary>
    [Fact]
    public void Inline_images_served_via_virtual_host_not_base64_data_uri()
    {
        var gateway = ReadRepoFile("src/SiNet.Infrastructure.Google/GmailEmailGateway.cs");
        var renderer = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/WebView2EmailBodyRenderer.cs");

        // Gateway fetches inline image bytes for cids referenced in the HTML body.
        Assert.Contains("Messages.Attachments.Get", gateway, StringComparison.Ordinal);
        Assert.Contains("InlineImages", gateway, StringComparison.Ordinal);
        Assert.Contains("cid:", gateway, StringComparison.Ordinal);

        // Renderer rewrites cid → virtual host and serves bytes via WebResourceRequested.
        Assert.Contains("AddWebResourceRequestedFilter", renderer, StringComparison.Ordinal);
        Assert.Contains("WebResourceRequested", renderer, StringComparison.Ordinal);
        Assert.Contains("CreateWebResourceResponse", renderer, StringComparison.Ordinal);
        Assert.Contains("RewriteInlineCidSources", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("data:image", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void Tag_picker_loads_all_outsidedata_types_with_optional_filter()
    {
        var tagging = ReadRepoFile("../SiNetSQL/SiNetSQL/Services/EmailIngestion/AttachmentTaggingService.cs");
        var picker = ReadRepoFile("SiNetProjectManagerV2/Services/EmailIngestion/AttachmentProjectFilePicker.cs");
        var standaloneHost = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/WpfEmailAttachmentProjectFilePickerHost.cs");
        var windowXaml = ReadRepoFile("src/SiNet.App.Wpf/Shared/Pickers/FileTreePickerWindow.xaml");
        var windowCs = ReadRepoFile("src/SiNet.App.Wpf/Shared/Pickers/FileTreePickerWindow.xaml.cs");
        var sqlTargets = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Detail/SqlEmailAttachmentTaggingService.cs");

        Assert.Contains("typeProjIdFilter", tagging, StringComparison.Ordinal);
        Assert.Contains("LoadExternalMaterialJobTypesAsync", tagging, StringComparison.Ordinal);
        var strictMethod = tagging.Split("LoadTagTargetsAsync", 2)[0];
        Assert.DoesNotContain("TypeOfProjectInProject", strictMethod, StringComparison.Ordinal);

        Assert.Contains("ConfigureTypeFilter", picker, StringComparison.Ordinal);
        Assert.Contains("כל הסוגים", picker, StringComparison.Ordinal);
        Assert.Contains("includeTypePrefix", picker, StringComparison.Ordinal);

        Assert.Contains("ConfigureTypeFilter", standaloneHost, StringComparison.Ordinal);
        Assert.Contains("כל הסוגים", standaloneHost, StringComparison.Ordinal);
        Assert.Contains("LoadTagPickerCatalogAsync", standaloneHost, StringComparison.Ordinal);
        Assert.Contains("FileTreePickerWindow", standaloneHost, StringComparison.Ordinal);

        Assert.Contains("TypeFilterBox", windowXaml, StringComparison.Ordinal);
        Assert.Contains("סוג פרויקט:", windowXaml, StringComparison.Ordinal);
        Assert.Contains("ReplaceRoots", windowCs, StringComparison.Ordinal);

        Assert.Contains("pf.OutSidData == true", sqlTargets, StringComparison.Ordinal);
        Assert.Contains("LoadTagPickerCatalogAsync", sqlTargets, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeOfProjectInProject", sqlTargets, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var candidate = Path.Combine(ResolveRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(candidate);
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "EMAIL_DETAIL_COMPONENT.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
