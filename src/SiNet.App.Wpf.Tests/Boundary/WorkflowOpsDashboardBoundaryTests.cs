using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards for Workflow Ops Dashboard — Application ports only, no direct SQL.
/// </summary>
public sealed class WorkflowOpsDashboardBoundaryTests
{
    private static readonly string[] ForbiddenIdentifiers =
    [
        "SiNetSQL",
        "IDbContextFactory",
        "SaveChanges",
        "Add-Migration",
    ];

    [Fact]
    public void ViewModel_uses_query_command_and_recovery_ports()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Admin/WorkflowOps/WorkflowOpsDashboardViewModel.cs");

        Assert.Contains("IWorkflowQueryService", source, StringComparison.Ordinal);
        Assert.Contains("GetAllWorkflowInstanceSnapshotsAsync", source, StringComparison.Ordinal);
        Assert.Contains("DetectStalledAsync", source, StringComparison.Ordinal);
        Assert.Contains("IWorkflowCommandService", source, StringComparison.Ordinal);
        Assert.Contains("AttemptRecoveryAsync", source, StringComparison.Ordinal);
        Assert.Contains("IRuntimeSubsystemStatusService", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.WorkflowOpsRetry", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.WorkflowOpsCancel", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.WorkflowOpsStart", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_viewmodel_uses_command_port_and_feature_gates()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Admin/WorkflowOps/WorkflowInstanceDetailViewModel.cs");

        Assert.Contains("IWorkflowCommandService", source, StringComparison.Ordinal);
        Assert.Contains("AdvanceAsync", source, StringComparison.Ordinal);
        Assert.Contains("PauseAsync", source, StringComparison.Ordinal);
        Assert.Contains("CancelAsync", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.WorkflowOpsAdvance", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.WorkflowOpsCancel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IDbContextFactory", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_dialog_uses_policy_and_start_command()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Admin/WorkflowOps/WorkflowStartDialogViewModel.cs");

        Assert.Contains("IProjectWorkflowPolicyService", source, StringComparison.Ordinal);
        Assert.Contains("GetAllowedWorkflowsAsync", source, StringComparison.Ordinal);
        Assert.Contains("StartAsync", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.WorkflowOpsStart", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShell_gates_workflow_ops_menu_on_feature_code()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("בריאות תהליכים", source, StringComparison.Ordinal);
        Assert.Contains("AppFeatureCodes.ShellOpenWorkflowOpsDashboard", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeWorkflowOpsDashboard", source, StringComparison.Ordinal);
        Assert.Contains("WorkflowOpsDashboardWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Di_registers_workflow_ops_window_and_viewmodel()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/NewSystemWpfServiceCollectionExtensions.cs");
        Assert.Contains("WorkflowOpsDashboardViewModel", source, StringComparison.Ordinal);
        Assert.Contains("WorkflowOpsDashboardWindow", source, StringComparison.Ordinal);
        Assert.Contains("WorkflowStartDialogViewModel", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_wpf_has_no_project_reference_to_sinetsql()
    {
        var csproj = ReadRepoFile("src/SiNet.App.Wpf/SiNet.App.Wpf.csproj");
        Assert.DoesNotContain("SiNetSQL", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Workflow_ops_sources_forbid_direct_sql_identifiers()
    {
        foreach (var relativePath in EnumerateWorkflowOpsFiles())
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

    private static IEnumerable<string> EnumerateWorkflowOpsFiles()
    {
        var dir = FindRepoRoot();
        var folder = Path.Combine(dir, "src", "SiNet.App.Wpf", "Admin", "WorkflowOps");
        foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.GetRelativePath(dir, file).Replace('\\', '/');
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("SiNet.sln not found");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
