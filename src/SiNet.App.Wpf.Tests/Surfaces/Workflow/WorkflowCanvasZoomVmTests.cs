using SiNet.App.Wpf.Surfaces.Workflow;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Workflow;

public sealed class WorkflowCanvasZoomVmTests
{
    [Fact]
    public void ZoomLevel_DefaultsTo100Percent()
    {
        var vm = new WorkflowVisualCanvasViewModel();

        Assert.Equal(1.0, vm.ZoomLevel);
        Assert.Equal("100%", vm.ZoomPercentText);
    }

    [Fact]
    public void SetZoom_ClampsToMinAndMax()
    {
        var vm = new WorkflowVisualCanvasViewModel();

        vm.SetZoom(0.1);
        Assert.Equal(WorkflowVisualCanvasViewModel.MinZoom, vm.ZoomLevel);
        Assert.Equal("40%", vm.ZoomPercentText);

        vm.SetZoom(10.0);
        Assert.Equal(WorkflowVisualCanvasViewModel.MaxZoom, vm.ZoomLevel);
        Assert.Equal("250%", vm.ZoomPercentText);
    }

    [Fact]
    public void AdjustZoom_MultipliesAndClamps()
    {
        var vm = new WorkflowVisualCanvasViewModel();

        vm.AdjustZoom(WorkflowVisualCanvasViewModel.ZoomStepFactor);
        Assert.Equal(1.1, vm.ZoomLevel, precision: 6);
        Assert.Equal("110%", vm.ZoomPercentText);

        vm.SetZoom(WorkflowVisualCanvasViewModel.MaxZoom);
        vm.AdjustZoom(WorkflowVisualCanvasViewModel.ZoomStepFactor);
        Assert.Equal(WorkflowVisualCanvasViewModel.MaxZoom, vm.ZoomLevel);
    }

    [Fact]
    public void ZoomResetCommand_ReturnsTo100Percent()
    {
        var vm = new WorkflowVisualCanvasViewModel();
        vm.SetZoom(2.0);

        Assert.True(vm.ZoomResetCommand.CanExecute(null));
        vm.ZoomResetCommand.Execute(null);

        Assert.Equal(1.0, vm.ZoomLevel);
        Assert.Equal("100%", vm.ZoomPercentText);
    }
}
