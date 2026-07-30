using System.IO;
using System.Windows;
using SiNet.App.Wpf.Surfaces.Tasks;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class TaskSurfaceWindowLayoutTests
{
    [Fact]
    public void Complementary_bounds_fill_work_area_minus_right_strip()
    {
        var workArea = new Rect(0, 0, 1920, 1080);

        var bounds = TaskSurfaceWindowLayout.ComputeComplementaryBounds(
            workArea,
            reservedRightWidth: TaskWorkbenchView.DefaultNarrowWidth,
            minWidth: 720);

        Assert.Equal(0, bounds.Left);
        Assert.Equal(0, bounds.Top);
        Assert.Equal(1520, bounds.Width);
        Assert.Equal(1080, bounds.Height);
    }

    [Fact]
    public void Complementary_bounds_respect_min_width_on_small_work_area()
    {
        var workArea = new Rect(100, 50, 900, 700);

        var bounds = TaskSurfaceWindowLayout.ComputeComplementaryBounds(
            workArea,
            reservedRightWidth: 400,
            minWidth: 720);

        Assert.Equal(100, bounds.Left);
        Assert.Equal(50, bounds.Top);
        Assert.Equal(720, bounds.Width);
        Assert.Equal(700, bounds.Height);
    }

    [Fact]
    public void Task_surface_open_paths_apply_complementary_layout()
    {
        var floatingHost = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/ProjectWork/ProjectWorkTaskFloatingHost.cs");
        var launcher = ReadRepoFile("src/SiNet.App.Wpf/WorkSurfaces/WorkSurfaceLauncher.cs");
        var emailWindow = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Email/EmailWorkItemWindow.xaml.cs");
        var inspection = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Inspection/InspectionWindowView.xaml.cs");

        Assert.Contains("TaskSurfaceWindowLayout.ApplyComplementaryToWorkbench", floatingHost, StringComparison.Ordinal);
        Assert.Contains("TaskSurfaceWindowLayout.ApplyComplementaryToWorkbench", launcher, StringComparison.Ordinal);
        Assert.Contains("TaskSurfaceWindowLayout.ApplyComplementaryToWorkbench", emailWindow, StringComparison.Ordinal);
        Assert.Contains("TaskSurfaceWindowLayout.ApplyComplementaryToWorkbench", inspection, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowStartupLocation.CenterOwner", floatingHost, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
