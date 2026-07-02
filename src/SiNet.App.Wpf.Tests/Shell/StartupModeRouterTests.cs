using SiNet.App.Wpf.Shell;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

/// <summary>
/// Unit tests for <see cref="StartupModeRouter"/>, the pure decision helper behind the first-startup
/// mode choice (see <c>docs/APP_SHELL.md</c> §2/§3).
/// </summary>
public sealed class StartupModeRouterTests
{
    [Fact]
    public void New_system_startup_is_disabled_by_default()
    {
        Assert.False(StartupModeRouter.EnableNewSystemStartup);
    }

    [Fact]
    public void Resolve_always_returns_legacy_while_new_system_startup_disabled()
    {
        Assert.Equal(StartupMode.Legacy, StartupModeRouter.Resolve(runNewSystem: true));
        Assert.Equal(StartupMode.Legacy, StartupModeRouter.Resolve(runNewSystem: false));
    }

    [Fact]
    public void OpensNewShell_is_false_while_new_system_startup_disabled()
    {
        Assert.False(StartupModeRouter.OpensNewShell(StartupMode.NewSystem));
        Assert.False(StartupModeRouter.OpensNewShell(StartupMode.Legacy));
    }

    [Theory]
    [InlineData(StartupMode.Legacy, true)]
    [InlineData(StartupMode.NewSystem, true)]
    public void OpensLegacyMainWindow_while_new_system_startup_disabled(StartupMode mode, bool expected)
    {
        Assert.Equal(expected, StartupModeRouter.OpensLegacyMainWindow(mode));
    }

    [Fact]
    public void Exactly_one_surface_opens_while_new_system_startup_disabled()
    {
        foreach (var runNewSystem in new[] { true, false })
        {
            var mode = StartupModeRouter.Resolve(runNewSystem);

            Assert.Equal(StartupMode.Legacy, mode);
            Assert.False(StartupModeRouter.OpensNewShell(mode));
            Assert.True(StartupModeRouter.OpensLegacyMainWindow(mode));
        }
    }
}
