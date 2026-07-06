using System.IO;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.WorkSurfaces;
using SiNet.Infrastructure.Sql.Services.Tasks;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class InspectionComponentKeyAlignmentTests
{
    [Fact]
    public void Inspection_shell_component_key_matches_task_registry()
    {
        Assert.Equal(TaskComponentKeys.InspectionReport, WorkSurfaceComponentKeys.InspectionReport);
        Assert.Equal(WorkSurfaceComponentKeys.InspectionReport, InspectionShellViewModel.InspectionComponentKey);
    }
}