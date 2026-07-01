using SiNet.App.Wpf.Shell;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

/// <summary>
/// Unit tests for <see cref="StartupModeSelectionViewModel"/>, the WPF-window-free logic behind the
/// first-visible startup mode chooser (see <c>docs/APP_SHELL.md</c> §2/§3). Startup mode is a user
/// decision, not a splash: these tests lock in that the default is New System, that nothing is chosen
/// automatically (no timer / no auto-confirm), and that each selection routes to the correct startup
/// surface via <see cref="StartupModeRouter"/> (New System never drags in the legacy main window).
/// </summary>
public sealed class StartupModeSelectionViewModelTests
{
    [Fact]
    public void Default_selected_mode_is_new_system()
    {
        var vm = new StartupModeSelectionViewModel();

        Assert.Equal(StartupMode.NewSystem, vm.SelectedMode);
        Assert.Equal(StartupMode.NewSystem, StartupModeSelectionViewModel.DefaultMode);
        Assert.True(vm.IsNewSystemSelected);
        Assert.False(vm.IsLegacySelected);
    }

    [Fact]
    public void Is_not_confirmed_until_user_confirms()
    {
        // No timeout / no automatic selection: a freshly created chooser has NOT confirmed anything.
        // The app must wait for an explicit Continue; it must never auto-continue after a delay.
        var vm = new StartupModeSelectionViewModel();

        Assert.False(vm.Confirmed);
    }

    [Fact]
    public void Confirm_marks_confirmed_and_raises_event()
    {
        var vm = new StartupModeSelectionViewModel();
        var raised = 0;
        vm.ConfirmRequested += (_, _) => raised++;

        vm.Confirm();

        Assert.True(vm.Confirmed);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Continue_command_confirms_the_current_selection()
    {
        var vm = new StartupModeSelectionViewModel();

        Assert.True(vm.ContinueCommand.CanExecute(null));
        vm.ContinueCommand.Execute(null);

        Assert.True(vm.Confirmed);
    }

    [Fact]
    public void Selecting_legacy_updates_mode_and_radio_flags()
    {
        var vm = new StartupModeSelectionViewModel
        {
            IsLegacySelected = true
        };

        Assert.Equal(StartupMode.Legacy, vm.SelectedMode);
        Assert.True(vm.IsLegacySelected);
        Assert.False(vm.IsNewSystemSelected);
    }

    [Fact]
    public void Selecting_new_system_updates_mode_and_radio_flags()
    {
        var vm = new StartupModeSelectionViewModel
        {
            IsLegacySelected = true
        };

        vm.IsNewSystemSelected = true;

        Assert.Equal(StartupMode.NewSystem, vm.SelectedMode);
        Assert.True(vm.IsNewSystemSelected);
        Assert.False(vm.IsLegacySelected);
    }

    [Fact]
    public void New_system_selection_routes_to_new_shell_and_skips_legacy_gates()
    {
        var vm = new StartupModeSelectionViewModel(); // default New System

        // New System opens the clean shell...
        Assert.True(StartupModeRouter.OpensNewShell(vm.SelectedMode));
        // ...and does NOT open the legacy main window (i.e. it bypasses the legacy startup flow/gates).
        Assert.False(StartupModeRouter.OpensLegacyMainWindow(vm.SelectedMode));
    }

    [Fact]
    public void Legacy_selection_routes_to_legacy_startup_flow()
    {
        var vm = new StartupModeSelectionViewModel
        {
            IsLegacySelected = true
        };

        // Legacy opens the legacy main window (existing startup flow)...
        Assert.True(StartupModeRouter.OpensLegacyMainWindow(vm.SelectedMode));
        // ...and does NOT open the clean shell.
        Assert.False(StartupModeRouter.OpensNewShell(vm.SelectedMode));
    }
}
