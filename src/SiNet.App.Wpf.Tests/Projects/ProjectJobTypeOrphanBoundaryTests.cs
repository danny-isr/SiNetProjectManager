using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

/// <summary>Source guards for DEV-011 Layer C (job-type remove + orphan track).</summary>
public sealed class ProjectJobTypeOrphanBoundaryTests
{
    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root (SiNet.sln).");
    }

    [Fact]
    public void Update_service_exposes_removal_risk_query()
    {
        var port = ReadRepoFile("src/SiNet.Application/Projects/IProjectUpdateService.cs");
        Assert.Contains("GetJobTypeRemovalRiskAsync", port, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_marks_orphan_notes_without_deleting_instances()
    {
        var sql = ReadRepoFile("src/SiNet.Infrastructure.Sql/Services/Projects/SqlProjectUpdateService.cs");
        Assert.Contains("WorkflowOrphanTrackMarkers.PrependMarker", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkflowInstances.Remove", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_dialog_warns_before_removing_types_with_open_workflows()
    {
        var vm = ReadRepoFile("src/SiNet.App.Wpf/Shared/Projects/ProjectEditDialogViewModel.cs");
        Assert.Contains("GetJobTypeRemovalRiskAsync", vm, StringComparison.Ordinal);
        Assert.Contains("אזהרה — הסרת סוג פרויקט", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void Ops_dashboard_filters_orphan_tracks()
    {
        var ops = ReadRepoFile("src/SiNet.App.Wpf/Admin/WorkflowOps/WorkflowOpsDashboardViewModel.cs");
        Assert.Contains("מסלול יתום (סוג הוסר)", ops, StringComparison.Ordinal);
        Assert.Contains("IsOrphanTrack", ops, StringComparison.Ordinal);
    }
}
