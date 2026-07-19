using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Wave 2–4 wiring guardrails for ProjectWork manual QA resume (code presence + cutover).
/// </summary>
public sealed class ProjectWorkManualQaBoundaryTests
{
    [Fact]
    public void Wave2_tree_open_routes_acc_drive_and_local()
    {
        var tree = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/ProjectWork/ProjectWorkTreeViewModel.cs");
        Assert.Contains("OpenedInAcc", tree, StringComparison.Ordinal);
        Assert.Contains("GoogleDrive", tree, StringComparison.Ordinal);
        Assert.Contains("SetOpenPreferenceAsync", tree, StringComparison.Ordinal);
        Assert.Contains("HandleFileDropAsync", tree, StringComparison.Ordinal);
        Assert.Contains("ExtensionConflict", tree, StringComparison.Ordinal);
        Assert.Contains("ConfirmAndDeleteAsync", tree, StringComparison.Ordinal);
    }

    [Fact]
    public void Wave2_surface_hosts_acc_viewer_pane()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/ProjectWork/ProjectWorkWindowView.xaml");
        Assert.Contains("AccViewerHost", xaml, StringComparison.Ordinal);
        Assert.Contains("ScanStatus", xaml, StringComparison.Ordinal);
        Assert.Contains("HasExtensionConflict", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wave4_inspection_adapters_use_native_hubs()
    {
        var adapters = ReadRepoFile("SiNetProjectManagerV2/Services/V2InspectionHostAdapters.cs");
        Assert.Contains("IActiveFileQueryHub", adapters, StringComparison.Ordinal);
        Assert.Contains("IFileOpenHub", adapters, StringComparison.Ordinal);
        Assert.Contains("PickReviewedPlansAsync", adapters, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectWorkViewModel", adapters, StringComparison.Ordinal);
    }

    [Fact]
    public void Wave4_launcher_prefers_shell_project_work_host()
    {
        var launcher = ReadRepoFile("src/SiNet.App.Wpf/WorkSurfaces/WorkSurfaceLauncher.cs");
        Assert.Contains("IProjectWorkSurfaceHost", launcher, StringComparison.Ordinal);
        Assert.Contains("TryOpenFromTaskAsync", launcher, StringComparison.Ordinal);
        Assert.Contains("IsProjectWorkSurface", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void Wave1_v2_registers_composite_project_work_host()
    {
        var di = ReadRepoFile("SiNetProjectManagerV2/Services/Composition/NewSystemServiceCollectionExtensions.cs");
        Assert.Contains("IProjectWorkSurfaceHost", di, StringComparison.Ordinal);
        Assert.Contains("V2ProjectWorkSurfaceHost", di, StringComparison.Ordinal);

        var host = ReadRepoFile("SiNetProjectManagerV2/Services/V2ProjectWorkSurfaceHost.cs");
        Assert.Contains("ProjectWorkSurfaceHost", host, StringComparison.Ordinal);
        Assert.Contains("MainWindow", host, StringComparison.Ordinal);
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
