using System.IO;
using SiNet.Application.WorkSurfaces;
using Xunit;

namespace SiNet.App.Wpf.Tests.WorkSurfaces;

public sealed class WorkSurfaceComponentKeysTests
{
    [Fact]
    public void IsInspectionReportSurface_includes_ManagerReviewApproval()
    {
        Assert.True(WorkSurfaceComponentKeys.IsInspectionReportSurface(
            WorkSurfaceComponentKeys.InspectionReport));
        Assert.True(WorkSurfaceComponentKeys.IsInspectionReportSurface(
            WorkSurfaceComponentKeys.ManagerReviewApproval));
        Assert.False(WorkSurfaceComponentKeys.IsInspectionReportSurface(
            WorkSurfaceComponentKeys.EmailComposeToPlanner));
    }

    [Fact]
    public void WorkSurfaceLauncher_routes_ManagerReviewApproval_via_IsInspectionReportSurface()
    {
        var launcher = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "SiNet.App.Wpf",
            "WorkSurfaces",
            "WorkSurfaceLauncher.cs"));

        Assert.Contains(
            "WorkSurfaceComponentKeys.IsInspectionReportSurface(context.ComponentKey)",
            launcher,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "string.Equals(context.ComponentKey, WorkSurfaceComponentKeys.InspectionReport",
            launcher,
            StringComparison.Ordinal);
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
