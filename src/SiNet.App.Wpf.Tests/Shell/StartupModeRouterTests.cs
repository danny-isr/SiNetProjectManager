using SiNet.App.Wpf.Shell;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

/// <summary>
/// Unit tests for <see cref="StartupModeRouter"/>, the pure decision helper behind the first-startup
/// mode choice (see <c>docs/APP_SHELL.md</c> §2/§3). These are WPF-free tests that lock in the routing
/// contract: the modal chooser defaults to New system mode; only New system mode opens the clean shell,
/// and only Legacy mode opens the legacy main window.
/// </summary>
public sealed class StartupModeRouterTests
{
    [Fact]
    public void Resolve_returns_new_system_when_checkbox_checked()
    {
        Assert.Equal(StartupMode.NewSystem, StartupModeRouter.Resolve(runNewSystem: true));
    }

    [Fact]
    public void Resolve_returns_legacy_when_checkbox_unchecked()
    {
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Exactly_one_surface_opens_for_every_choice(bool runNewSystem)
    {
        // Guardrail: the two surfaces are mutually exclusive and total — every startup choice opens
        // exactly one window (never both, never neither). This is what keeps New system mode from
        // also dragging in the legacy MainWindow (docs/APP_SHELL.md §3).
        var mode = StartupModeRouter.Resolve(runNewSystem);

        Assert.NotEqual(StartupModeRouter.OpensNewShell(mode), StartupModeRouter.OpensLegacyMainWindow(mode));
    }
}
