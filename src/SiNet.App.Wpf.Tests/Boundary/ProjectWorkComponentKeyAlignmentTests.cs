using SiNet.Application.WorkSurfaces;
using SiNet.Infrastructure.Sql.Services.Tasks;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards that the native ProjectWork surface keys stay aligned with the task navigation registry
/// keys, mirroring <see cref="InspectionComponentKeyAlignmentTests"/>. The launcher routes these three
/// keys to the native ProjectWork task surface (Phase 5a).
/// </summary>
public sealed class ProjectWorkComponentKeyAlignmentTests
{
    [Fact]
    public void ProjectWork_surface_keys_match_task_registry()
    {
        Assert.Equal(TaskComponentKeys.ProjectWork, WorkSurfaceComponentKeys.ProjectWork);
        Assert.Equal(TaskComponentKeys.MaterialChecklist, WorkSurfaceComponentKeys.MaterialChecklist);
        Assert.Equal(TaskComponentKeys.PoliceSubmission, WorkSurfaceComponentKeys.PoliceSubmission);
    }

    [Theory]
    [InlineData(WorkSurfaceComponentKeys.ProjectWork)]
    [InlineData(WorkSurfaceComponentKeys.MaterialChecklist)]
    [InlineData(WorkSurfaceComponentKeys.PoliceSubmission)]
    public void IsProjectWorkSurface_recognizes_all_three_keys(string key)
    {
        Assert.True(WorkSurfaceComponentKeys.IsProjectWorkSurface(key));
    }

    [Theory]
    [InlineData(WorkSurfaceComponentKeys.InspectionReport)]
    [InlineData(WorkSurfaceComponentKeys.EmailFiling)]
    [InlineData(null)]
    public void IsProjectWorkSurface_rejects_other_keys(string? key)
    {
        Assert.False(WorkSurfaceComponentKeys.IsProjectWorkSurface(key));
    }
}
