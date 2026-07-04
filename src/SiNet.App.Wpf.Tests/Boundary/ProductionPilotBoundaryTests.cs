using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards for <see cref="docs/NEW_SYSTEM_PRODUCTION_READINESS.md"/> — limited production pilot envelope.
/// </summary>
public sealed class ProductionPilotBoundaryTests
{
    [Fact]
    public void Production_readiness_doc_defines_pilot_envelope()
    {
        var doc = ReadRepoFile("docs/NEW_SYSTEM_PRODUCTION_READINESS.md");

        Assert.Contains("Limited Production Pilot", doc, StringComparison.Ordinal);
        Assert.Contains("EmailWindowView", doc, StringComparison.Ordinal);
        Assert.Contains("InspectionShellView", doc, StringComparison.Ordinal);
        Assert.Contains("Read-only", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GmailSend", doc, StringComparison.Ordinal);
        Assert.Contains("ACC upload", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void New_shell_factory_wraps_inspection_harness_menu_in_debug_only()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("#if DEBUG", source, StringComparison.Ordinal);
        Assert.Contains("OpenInspectionShell,", source, StringComparison.Ordinal);
        Assert.Contains("developer harness", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("קריאה בלבד", source, StringComparison.Ordinal);

        var debugIdx = source.IndexOf("#if DEBUG", StringComparison.Ordinal);
        var inspectionMenuIdx = source.IndexOf("OpenInspectionShell,", StringComparison.Ordinal);
        var endifIdx = source.IndexOf("#endif", inspectionMenuIdx, StringComparison.Ordinal);
        Assert.True(debugIdx >= 0 && inspectionMenuIdx > debugIdx && endifIdx > inspectionMenuIdx);
    }

    [Fact]
    public void Email_window_view_model_hides_and_disables_deferred_production_actions()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowViewModel.cs");

        Assert.Contains("ShowDeferredWriteActions => false", source, StringComparison.Ordinal);
        Assert.Contains("DeferredProductionPilotAction", source, StringComparison.Ordinal);
        Assert.Contains("() => false", source, StringComparison.Ordinal);
        Assert.Contains("G-Policy", source, StringComparison.Ordinal);
        Assert.Contains("ITaskCompletionCoordinator", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_window_view_hides_deferred_action_regions_in_production_pilot()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView.xaml");

        Assert.Contains("ShowDeferredWriteActions", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding ShowDeferredWriteActions", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAttachmentCommand", xaml, StringComparison.Ordinal);
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
