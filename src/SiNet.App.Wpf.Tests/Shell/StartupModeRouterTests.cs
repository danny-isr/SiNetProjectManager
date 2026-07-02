using SiNet.App.Wpf.Shell;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

/// <summary>
/// Unit tests for startup mode selection and routing (see <c>docs/APP_SHELL.md</c> §3).
/// </summary>
public sealed class StartupModeRouterTests
{
    [Fact]
    public void StartupModeSelectionViewModel_defaults_to_New_System()
    {
        var vm = new StartupModeSelectionViewModel();

        Assert.Equal(StartupMode.NewSystem, vm.SelectedMode);
        Assert.True(vm.IsNewSystemSelected);
        Assert.False(vm.IsLegacySelected);
    }

    [Fact]
    public void Resolve_maps_boolean_to_startup_mode()
    {
        Assert.Equal(StartupMode.NewSystem, StartupModeRouter.Resolve(runNewSystem: true));
        Assert.Equal(StartupMode.Legacy, StartupModeRouter.Resolve(runNewSystem: false));
    }

    [Theory]
    [InlineData(StartupMode.NewSystem, true)]
    [InlineData(StartupMode.Legacy, false)]
    public void OpensNewShell_only_for_new_system_mode(StartupMode mode, bool expected)
    {
        Assert.Equal(expected, StartupModeRouter.OpensNewShell(mode));
    }

    [Theory]
    [InlineData(StartupMode.Legacy, true)]
    [InlineData(StartupMode.NewSystem, false)]
    public void OpensLegacyMainWindow_only_for_legacy_mode(StartupMode mode, bool expected)
    {
        Assert.Equal(expected, StartupModeRouter.OpensLegacyMainWindow(mode));
    }

    [Fact]
    public void Exactly_one_surface_opens_for_every_choice()
    {
        foreach (var mode in new[] { StartupMode.NewSystem, StartupMode.Legacy })
        {
            Assert.NotEqual(StartupModeRouter.OpensNewShell(mode), StartupModeRouter.OpensLegacyMainWindow(mode));
        }
    }

    [Fact]
    public void Router_has_no_EnableNewSystemStartup_master_switch()
    {
        Assert.Null(typeof(StartupModeRouter).GetProperty("EnableNewSystemStartup"));
        Assert.Null(typeof(StartupModeRouter).GetField("EnableNewSystemStartup"));
    }
}
