using System.IO;
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
        var detailVmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailDetailViewModel.cs");
        var detailFolder = Path.Combine(
            ResolveRepoRoot(),
            "src/SiNet.App.Wpf/Surfaces/Email/Detail");

        Assert.Contains("EmailDetailView", windowXaml, StringComparison.Ordinal);
        Assert.Contains("Binding EmailDetail", windowXaml, StringComparison.Ordinal);
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
        Assert.Contains("AddSiNetEmailDetailSql", v2Graph, StringComparison.Ordinal);
        Assert.Contains("IEmailMoveToProjectService", extensions, StringComparison.Ordinal);
        Assert.Contains("IEmailAccIngestionService", extensions, StringComparison.Ordinal);
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

    [Fact]
    public void Workflow_association_ignores_global_project_override()
    {
        var sql = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Detail/SqlEmailWorkflowServices.cs");
        Assert.DoesNotContain("query.OverrideProjectId ?? message.ProjectId", sql, StringComparison.Ordinal);
        Assert.Contains("ResolveDefaultOfficeProjectIdAsync", sql, StringComparison.Ordinal);
        Assert.Contains("defaultOfficeProjectId", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Tag_picker_loads_all_outsidedata_types_with_optional_filter()
    {
        var tagging = ReadRepoFile("../SiNetSQL/SiNetSQL/Services/EmailIngestion/AttachmentTaggingService.cs");
        var picker = ReadRepoFile("SiNetProjectManagerV2/Services/EmailIngestion/AttachmentProjectFilePicker.cs");
        var windowXaml = ReadRepoFile("SiNetProjectManagerV2/Windows/FileTreePickerWindow.xaml");
        var windowCs = ReadRepoFile("SiNetProjectManagerV2/Windows/FileTreePickerWindow.xaml.cs");
        var sqlTargets = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Email/Detail/SqlEmailAttachmentTaggingService.cs");

        Assert.Contains("typeProjIdFilter", tagging, StringComparison.Ordinal);
        Assert.Contains("LoadExternalMaterialJobTypesAsync", tagging, StringComparison.Ordinal);
        var strictMethod = tagging.Split("LoadTagTargetsAsync", 2)[0];
        Assert.DoesNotContain("TypeOfProjectInProject", strictMethod, StringComparison.Ordinal);

        Assert.Contains("ConfigureTypeFilter", picker, StringComparison.Ordinal);
        Assert.Contains("כל הסוגים", picker, StringComparison.Ordinal);
        Assert.Contains("includeTypePrefix", picker, StringComparison.Ordinal);

        Assert.Contains("TypeFilterBox", windowXaml, StringComparison.Ordinal);
        Assert.Contains("סוג פרויקט:", windowXaml, StringComparison.Ordinal);
        Assert.Contains("ReplaceRoots", windowCs, StringComparison.Ordinal);

        Assert.Contains("pf.OutSidData == true", sqlTargets, StringComparison.Ordinal);
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
