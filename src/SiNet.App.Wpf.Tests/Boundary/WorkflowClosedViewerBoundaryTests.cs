using System.IO;
using SiNet.App.Wpf.Surfaces.Workflow;
using SiNet.Application.Workflow;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards for the native closed-world workflow viewer — Application ports only, no V2 Dialogs host wrap.
/// </summary>
public sealed class WorkflowClosedViewerBoundaryTests
{
    private static readonly string[] ForbiddenIdentifiers =
    [
        "WorkflowManagementWindow",
        "LegacyWorkflowClosedViewerWindowFactory",
        "SiNetSQL",
        "SiNetProjectManagerV2.Dialogs",
        "IDbContextFactory",
        "SaveChanges",
    ];

    [Fact]
    public void ViewModel_uses_IWorkflowClosedViewerQueryService()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Workflow/WorkflowClosedViewerViewModel.cs");

        Assert.Contains("IWorkflowClosedViewerQueryService", source, StringComparison.Ordinal);
        Assert.Contains("GetDefinitionGraphsAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetCatalogsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShell_opens_native_workflow_viewer_not_legacy_management_window()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("IWorkflowClosedViewerWindowFactory", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.ShellOpenWorkflowClosedViewer", source, StringComparison.Ordinal);
        Assert.Contains("workflowViewerFactory.Create()", source, StringComparison.Ordinal);
        Assert.Contains("ShowWindow(workflowViewerFactory.Create())", source, StringComparison.Ordinal);
        Assert.Contains("צפייה בתהליכים (סגור)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkflowManagementWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyWorkflowClosedViewerWindowFactory", source, StringComparison.Ordinal);
    }

    [Fact]
    public void New_system_wpf_registers_workflow_closed_viewer()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/NewSystemWpfServiceCollectionExtensions.cs");
        Assert.Contains("AddSiNetWorkflowClosedViewer()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_creates_WorkflowVisualCanvasWindow()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Workflow/WorkflowClosedViewerWindowFactory.cs");
        Assert.Contains("WorkflowVisualCanvasWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkflowManagementWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_surface_sources_forbid_legacy_host_identifiers()
    {
        foreach (var relativePath in EnumerateWorkflowSurfaceFiles())
        {
            var content = ReadRepoFile(relativePath);
            foreach (var forbidden in ForbiddenIdentifiers)
            {
                Assert.False(
                    content.Contains(forbidden, StringComparison.Ordinal),
                    $"'{forbidden}' found in {relativePath}");
            }
        }
    }

    [Fact]
    public async Task Dry_run_rejects_out_of_catalog_action_type()
    {
        var sut = new WorkflowClosedViewerViewModel(new StubWorkflowClosedViewerQueryService());
        await sut.LoadAsync();

        var before = sut.DryRunActionType;
        var statusBefore = sut.StatusMessage;
        sut.DryRunActionType = "NotARealActionType";

        Assert.Equal(before, sut.DryRunActionType);
        Assert.Equal(statusBefore, sut.StatusMessage);
    }

    [Fact]
    public async Task Load_builds_tree_from_query_graphs()
    {
        var sut = new WorkflowClosedViewerViewModel(new StubWorkflowClosedViewerQueryService());
        await sut.LoadAsync();

        Assert.Single(sut.Roots);
        Assert.IsType<WorkflowDefViewerNode>(sut.Roots[0]);
        Assert.Contains("1 תהליכים", sut.StatusMessage, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateWorkflowSurfaceFiles()
    {
        var dir = Path.Combine(ResolveRepoRoot(), "src", "SiNet.App.Wpf", "Surfaces", "Workflow");
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.GetRelativePath(ResolveRepoRoot(), file).Replace('\\', '/');
            }
        }
    }

    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(Path.Combine(ResolveRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "UI_WINDOW_MIGRATION_MAP.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class StubWorkflowClosedViewerQueryService : IWorkflowClosedViewerQueryService
    {
        public Task<IReadOnlyList<WorkflowDefinitionGraphDto>> GetDefinitionGraphsAsync(
            CancellationToken cancellationToken = default)
        {
            var stage = new WorkflowStageGraphDto(
                1, "S1", "Stage One", null, 1, true, false, "Stage", true, true,
                null, null, null, null, 40, 40,
                Array.Empty<WorkflowStageTaskGraphDto>());

            var graph = new WorkflowDefinitionGraphDto(
                10, "Demo", "Demo Workflow", "desc", true, true,
                new[] { stage },
                Array.Empty<WorkflowTransitionGraphDto>());

            return Task.FromResult<IReadOnlyList<WorkflowDefinitionGraphDto>>(new[] { graph });
        }

        public Task<WorkflowClosedWorldCatalogDto> GetCatalogsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkflowClosedWorldCatalogDto(
                new[] { "Stage", "Decision" },
                new[] { "CreateStageTasks", "ClosePreviousStageTasks" },
                new[] { "Manual" },
                new[] { "Always" },
                new[] { "Manual" },
                new[] { "Active" },
                new[] { "Approved" },
                new[] { "Demo" },
                new[] { "S1" }));
    }
}
