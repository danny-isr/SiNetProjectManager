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

    [Theory]
    [InlineData("InspectionReport", null)]
    [InlineData(null, "SendReportToPlanner")]
    [InlineData(null, "SendInternalApproval")]
    public void EmailComposeToPlanner_with_report_bound_context_routes_to_inspection(
        string? entityType,
        string? taskTypeCode)
    {
        Assert.True(WorkSurfaceComponentKeys.ShouldRouteEmailComposeToInspection(
            WorkSurfaceComponentKeys.EmailComposeToPlanner,
            entityType,
            taskTypeCode));
    }

    [Fact]
    public void EmailComposeToPlanner_with_email_target_does_not_route_to_inspection()
    {
        Assert.False(WorkSurfaceComponentKeys.ShouldRouteEmailComposeToInspection(
            WorkSurfaceComponentKeys.EmailComposeToPlanner,
            "EmailInboxMessage",
            "UnrelatedEmailTask"));
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
        Assert.Contains(
            "WorkSurfaceComponentKeys.ShouldRouteEmailComposeToInspection(",
            launcher,
            StringComparison.Ordinal);
        Assert.Contains(
            "context = context with { ComponentKey = WorkSurfaceComponentKeys.InspectionReport };",
            launcher,
            StringComparison.Ordinal);
    }

    [Fact]
    public void InspectionWindowViewModel_accepts_ManagerReviewApproval_component_key()
    {
        var vm = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "SiNet.App.Wpf",
            "Surfaces",
            "Inspection",
            "InspectionWindowViewModel.cs"));

        Assert.Contains(
            "WorkSurfaceComponentKeys.IsInspectionReportSurface(context.ComponentKey)",
            vm,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "string.Equals(context.ComponentKey, WorkSurfaceComponentKeys.InspectionReport",
            vm,
            StringComparison.Ordinal);
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
