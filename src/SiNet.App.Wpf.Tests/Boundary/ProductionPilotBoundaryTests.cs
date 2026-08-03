using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards for <c>docs/NEW_SYSTEM_PRODUCTION_READINESS.md</c> — production cutover envelope.
/// </summary>
public sealed class ProductionPilotBoundaryTests
{
    [Fact]
    public void Production_readiness_doc_defines_pilot_envelope()
    {
        var doc = ReadRepoFile("docs/NEW_SYSTEM_PRODUCTION_READINESS.md");

        Assert.Contains("Production cutover", doc, StringComparison.Ordinal);
        Assert.Contains("SiNet.App.Wpf.exe", doc, StringComparison.Ordinal);
        Assert.Contains("ACC-filing", doc, StringComparison.Ordinal);
        Assert.Contains("InspectionShell", doc, StringComparison.Ordinal);
        Assert.Contains("GmailSend", doc, StringComparison.Ordinal);
        Assert.Contains("ReportsManagement", doc, StringComparison.Ordinal);
        Assert.Contains("StandaloneNew", doc, StringComparison.Ordinal);
        Assert.Contains("DESKTOP_CUTOVER", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void New_shell_factory_wraps_inspection_harness_menu_in_debug_only()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("#if DEBUG", source, StringComparison.Ordinal);
        Assert.Contains("OpenInspectionShell,", source, StringComparison.Ordinal);
        Assert.Contains("developer harness", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IEmailSurfaceHost", source, StringComparison.Ordinal);

        var debugIdx = source.IndexOf("#if DEBUG", StringComparison.Ordinal);
        var inspectionMenuIdx = source.IndexOf("OpenInspectionShell,", StringComparison.Ordinal);
        var endifIdx = source.IndexOf("#endif", inspectionMenuIdx, StringComparison.Ordinal);
        Assert.True(debugIdx >= 0 && inspectionMenuIdx > debugIdx && endifIdx > inspectionMenuIdx);
    }

    [Fact]
    public void Email_window_exposes_detail_component_with_action_bar()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailSurfaceView.xaml");
        var windowXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");
        var detailXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailDetailView.xaml");
        var actionBarXaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/Detail/EmailActionBarView.xaml");

        Assert.Contains("EmailDetailView", xaml, StringComparison.Ordinal);
        Assert.Contains("EmailSurfaceView", windowXaml, StringComparison.Ordinal);
        Assert.Contains("EmailActionBarView", detailXaml, StringComparison.Ordinal);
        Assert.Contains("MoveToProjectCommand", actionBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_window_view_uses_two_column_list_detail_layout()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailSurfaceView.xaml");

        Assert.Contains("EmailListView", xaml, StringComparison.Ordinal);
        Assert.Contains("EmailDetailView", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAttachmentCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDeferredWriteActions", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<DatePicker", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_window_view_model_exposes_clear_search_and_detail_shell()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.Contains("ClearSearchCommand", source, StringComparison.Ordinal);
        Assert.Contains("ClearSearchAsync", source, StringComparison.Ordinal);
        Assert.Contains("EmailDetailViewModel", source, StringComparison.Ordinal);
        Assert.Contains("ShowUnreadCount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_readiness_doc_defines_interactive_smoke_gate_and_operator_handoff()
    {
        var doc = ReadRepoFile("docs/NEW_SYSTEM_PRODUCTION_READINESS.md");

        Assert.Contains("## 9. Manual smoke", doc, StringComparison.Ordinal);
        Assert.Contains("Not Run", doc, StringComparison.Ordinal);
        Assert.Contains("Blocked by environment/config", doc, StringComparison.Ordinal);
        Assert.Contains("SMOKE_CUTOVER_SINET_APP_WPF.md", doc, StringComparison.Ordinal);
        Assert.Contains("Ready for 1–2 internal ACC-filing pilot users", doc, StringComparison.Ordinal);
        Assert.Contains("Email Composite Work Surface Contract", doc, StringComparison.Ordinal);
        Assert.Contains("G-Policy", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_surfaces_do_not_use_forbidden_google_or_acc_runtime()
    {
        foreach (var relativePath in EnumerateNativeSurfaceFiles())
        {
            var content = ReadRepoFile(relativePath);
            Assert.DoesNotContain("GmailClientProvider", content, StringComparison.Ordinal);
            Assert.DoesNotContain("GoogleService", content, StringComparison.Ordinal);
            Assert.DoesNotContain("IEmailSender", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Bim360Service", content, StringComparison.Ordinal);
            Assert.DoesNotContain("IAccFileUploadService", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Native_surfaces_do_not_mutate_workflow_directly_or_add_task_router()
    {
        foreach (var relativePath in EnumerateNativeSurfaceFiles())
        {
            var content = ReadRepoFile(relativePath);
            Assert.DoesNotContain("CheckAndAutoAdvanceAsync(", content, StringComparison.Ordinal);
            Assert.DoesNotContain("TaskWindowRouter", content, StringComparison.Ordinal);
            Assert.DoesNotContain("NewTaskWindowRouter", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void New_system_boundary_cross_references_production_readiness()
    {
        var doc = ReadRepoFile("docs/NEW_SYSTEM_BOUNDARY.md");
        Assert.Contains("NEW_SYSTEM_PRODUCTION_READINESS.md", doc, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateNativeSurfaceFiles()
    {
        foreach (var root in new[] { "src/SiNet.App.Wpf/Surfaces", "src/SiNet.App.Wpf/Autodesk", "src/SiNet.App.Wpf/Shell" })
        {
            var dir = Path.Combine(ResolveRepoRoot(), root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                yield return Path.GetRelativePath(ResolveRepoRoot(), file).Replace('\\', '/');
            }
        }
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
            if (File.Exists(Path.Combine(dir.FullName, "docs", "NEW_SYSTEM_PRODUCTION_READINESS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
