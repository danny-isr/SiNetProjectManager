using System.IO;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.App.Wpf.WebViewHosting;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class AccViewerHostRegistrationTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root with SiNet.sln not found.");
    }

    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    [Theory]
    [InlineData(10, 10)]
    [InlineData(3, 3)]
    [InlineData(0, WebView2AccViewerHost.DefaultMaxTabs)]
    [InlineData(-1, WebView2AccViewerHost.DefaultMaxTabs)]
    public void ResolveMaxTabs_uses_positive_or_default(int configured, int expected)
        => Assert.Equal(expected, WebView2AccViewerHost.ResolveMaxTabs(configured));

    [Fact]
    public void AddSiNetNewSystemWpf_registers_acc_viewer_host()
    {
        var wpfDi = ReadRepoFile("src/SiNet.App.Wpf/NewSystemWpfServiceCollectionExtensions.cs");
        Assert.Contains("IAccViewerHost", wpfDi, StringComparison.Ordinal);
        Assert.Contains("WebView2AccViewerHost", wpfDi, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IAccViewerHost, WebView2AccViewerHost>", wpfDi, StringComparison.Ordinal);
    }

    [Fact]
    public void V2_registers_app_wpf_acc_viewer_host()
    {
        var app = ReadRepoFile("SiNetProjectManagerV2/App.xaml.cs");
        Assert.Contains(
            "SiNet.App.Wpf.Surfaces.ProjectWork.WebView2AccViewerHost",
            app,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SiNetProjectManagerV2.Services.ProjectWork.WebView2AccViewerHost",
            app,
            StringComparison.Ordinal);
    }

    [Fact]
    public void App_wpf_acc_viewer_does_not_reference_v2_webview2_helper()
    {
        var host = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/ProjectWork/WebView2AccViewerHost.cs");
        var env = ReadRepoFile("src/SiNet.App.Wpf/WebViewHosting/WebView2SharedEnvironment.cs");
        Assert.DoesNotContain("WebView2Helper", host, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetProjectManagerV2", host, StringComparison.Ordinal);
        Assert.Contains(nameof(WebView2SharedEnvironment), host, StringComparison.Ordinal);
        Assert.Contains("\"SiNet\"", env, StringComparison.Ordinal);
        Assert.Contains("\"WebView2UserData\"", env, StringComparison.Ordinal);
        Assert.Contains("ChromeUserAgent", env, StringComparison.Ordinal);
    }

    [Fact]
    public void V2_legacy_acc_viewer_host_marked_obsolete()
    {
        var legacy = ReadRepoFile("SiNetProjectManagerV2/Services/ProjectWork/WebView2AccViewerHost.cs");
        Assert.Contains("Obsolete", legacy, StringComparison.Ordinal);
        Assert.Contains("pending removal", legacy, StringComparison.OrdinalIgnoreCase);
    }
}
