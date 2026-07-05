using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Documentation and source guards for
/// <see cref="docs/WORK_SURFACE_WORKFLOW_INTEGRATION.md"/> and related workflow/task boundaries.
/// </summary>
public sealed class WorkSurfaceWorkflowIntegrationBoundaryTests
{
    private static readonly string[] NativeSurfaceRoots =
    [
        "src/SiNet.App.Wpf/Surfaces",
        "src/SiNet.App.Wpf/Inspection",
        "src/SiNet.App.Wpf/Autodesk",
    ];

    private static readonly string[] ForbiddenRuntimeIdentifiersInNativeSurfaces =
    [
        "GmailClientProvider",
        "GoogleService",
        "IEmailSender",
        "Bim360Service",
        "MyOffice.AutodeskConnector",
        "IWorkflowCommandService.CheckAndAutoAdvance",
        "CheckAndAutoAdvanceAsync(",
        "IAccFileUploadService",
        "IAccFolderPathService",
        "TaskWindowRouter",
        "NewTaskWindowRouter",
    ];

    [Fact]
    public void Integration_doc_defines_canonical_task_navigation_and_completion_path()
    {
        var doc = ReadRepoFile("docs/WORK_SURFACE_WORKFLOW_INTEGRATION.md");

        Assert.Contains("TaskNavigationResolver", doc, StringComparison.Ordinal);
        Assert.Contains("ITaskNavigationService", doc, StringComparison.Ordinal);
        Assert.Contains("AddSiNetProcessBackbone", doc, StringComparison.Ordinal);
        Assert.Contains("SqlTaskNavigationService", doc, StringComparison.Ordinal);
        Assert.Contains("WorkSurfaceContext", doc, StringComparison.Ordinal);
        Assert.Contains("ITaskCompletionCoordinator", doc, StringComparison.Ordinal);
        Assert.Contains("IWorkflowCommandService", doc, StringComparison.Ordinal);
        Assert.Contains("No new router", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not", doc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Integration_doc_maps_email_inspection_projectwork_tasks_workflow_and_acc_surfaces()
    {
        var doc = ReadRepoFile("docs/WORK_SURFACE_WORKFLOW_INTEGRATION.md");

        Assert.Contains("EmailWindowView", doc, StringComparison.Ordinal);
        Assert.Contains("InspectionWindowView", doc, StringComparison.Ordinal);
        Assert.Contains("ProjectWork", doc, StringComparison.Ordinal);
        Assert.Contains("FloatingProjectTasksView", doc, StringComparison.Ordinal);
        Assert.Contains("WorkflowDashboard", doc, StringComparison.Ordinal);
        Assert.Contains("AccControlPlaneStatusWindow", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Integration_doc_records_two_open_modes_and_no_fallback_policy()
    {
        var doc = ReadRepoFile("docs/WORK_SURFACE_WORKFLOW_INTEGRATION.md");

        Assert.Contains("Project-centric", doc, StringComparison.Ordinal);
        Assert.Contains("Task-driven", doc, StringComparison.Ordinal);
        Assert.Contains("**No** first/last/default entity fallback", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Ui_window_migration_map_cross_references_integration_doc()
    {
        var doc = ReadRepoFile("docs/UI_WINDOW_MIGRATION_MAP.md");

        Assert.Contains("WORK_SURFACE_WORKFLOW_INTEGRATION.md", doc, StringComparison.Ordinal);
        Assert.Contains("ITaskCompletionCoordinator", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_map_cross_references_integration_doc()
    {
        var doc = ReadRepoFile("docs/MIGRATION_MAP.md");

        Assert.Contains("WORK_SURFACE_WORKFLOW_INTEGRATION.md", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void G_startup_remains_wired_in_v2_new_system_startup()
    {
        var source = ReadRepoFile("SiNetProjectManagerV2/App.xaml.cs");

        var runNewSystemStart = source.IndexOf("private void RunNewSystemStartup", StringComparison.Ordinal);
        var runNewSystemEnd = source.IndexOf("private static void LaunchNewSystemShell", StringComparison.Ordinal);
        Assert.True(runNewSystemStart >= 0);
        Assert.True(runNewSystemEnd > runNewSystemStart);

        var body = source.Substring(runNewSystemStart, runNewSystemEnd - runNewSystemStart);
        Assert.Contains("StartNewSystemConnectorAuthRestore()", body, StringComparison.Ordinal);

        var helperStart = source.IndexOf("StartNewSystemConnectorAuthRestore", StringComparison.Ordinal);
        var helperBody = source.Substring(helperStart);
        Assert.Contains("GetServices<IConnectorAuthService>()", helperBody, StringComparison.Ordinal);
        Assert.Contains("TryRestoreSessionAsync", helperBody, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginAsync", helperBody, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NativeSurfaceSourceFiles))]
    public void Native_surfaces_do_not_reference_forbidden_runtime_identifiers(string relativePath)
    {
        var content = ReadRepoFile(relativePath);

        foreach (var forbidden in ForbiddenRuntimeIdentifiersInNativeSurfaces)
        {
            Assert.False(
                content.Contains(forbidden, StringComparison.Ordinal),
                $"Forbidden identifier '{forbidden}' found in {relativePath}");
        }
    }

    public static IEnumerable<object[]> NativeSurfaceSourceFiles()
    {
        foreach (var root in NativeSurfaceRoots)
        {
            var dir = ResolveRepoPath(root);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                yield return [Path.GetRelativePath(ResolveRepoRoot(), file).Replace('\\', '/')];
            }
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var candidate = Path.Combine(ResolveRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(candidate);
    }

    private static string ResolveRepoPath(string relativePath) =>
        Path.Combine(ResolveRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                || File.Exists(Path.Combine(dir.FullName, "docs", "WORK_SURFACE_WORKFLOW_INTEGRATION.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
