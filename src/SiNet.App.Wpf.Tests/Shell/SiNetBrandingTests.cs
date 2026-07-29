using System.IO;
using SiNet.App.Wpf.Shell;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

public sealed class SiNetBrandingTests
{
    [Fact]
    public void Standalone_ships_shia_chadash_brand_assets()
    {
        var assets = Path.Combine(AppWpfRoot, "Assets");
        Assert.True(File.Exists(Path.Combine(assets, "sinet.ico")));
        Assert.True(File.Exists(Path.Combine(assets, "logo_si.jpg")));
        Assert.True(File.Exists(Path.Combine(assets, "shia-chadash-mark.png")));
        Assert.True(new FileInfo(Path.Combine(assets, "sinet.ico")).Length > 0);
    }

    [Fact]
    public void Csproj_sets_hebrew_company_metadata_and_assets()
    {
        var csproj = File.ReadAllText(Path.Combine(AppWpfRoot, "SiNet.App.Wpf.csproj"));
        Assert.Contains("ApplicationIcon>Assets\\sinet.ico", csproj, StringComparison.Ordinal);
        Assert.Contains("שיא חדש בע״מ", csproj, StringComparison.Ordinal);
        Assert.Contains("Assets\\logo_si.jpg", csproj, StringComparison.Ordinal);
        Assert.Contains("Assets\\shia-chadash-mark.png", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShell_header_uses_hebrew_company_branding_and_road_mark()
    {
        var vm = new NewShellViewModel([], "User");
        Assert.Equal("שיא חדש בע״מ", vm.Title);
        Assert.Contains("מנהל פרויקטים", vm.HeaderSubtitle, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNet", vm.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNet", vm.HeaderSubtitle, StringComparison.Ordinal);

        var xaml = File.ReadAllText(Path.Combine(AppWpfRoot, "Shell", "NewShellWindow.xaml"));
        Assert.Contains("shia-chadash-mark.png", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_splash_shows_company_logo_and_hebrew_name()
    {
        var splash = File.ReadAllText(Path.Combine(AppWpfRoot, "Shell", "StartupSplashWindow.xaml"));
        Assert.Contains("logo_si.jpg", splash, StringComparison.Ordinal);
        Assert.Contains("שיא חדש בע״מ", splash, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNet Project Manager", splash, StringComparison.Ordinal);

        var mode = File.ReadAllText(Path.Combine(AppWpfRoot, "Shell", "StartupModeSelectionWindow.xaml"));
        Assert.Contains("shia-chadash-mark.png", mode, StringComparison.Ordinal);
        Assert.Contains("שיא חדש בע״מ", mode, StringComparison.Ordinal);
    }

    [Fact]
    public void App_startup_shows_branded_splash()
    {
        var app = File.ReadAllText(Path.Combine(AppWpfRoot, "App.xaml.cs"));
        Assert.Contains("StartupSplashWindow", app, StringComparison.Ordinal);
        Assert.Contains("שיא חדש", app, StringComparison.Ordinal);
    }

    private static string AppWpfRoot =>
        Path.Combine(Boundary.RepoPaths.RepoRoot, "src", "SiNet.App.Wpf");
}
