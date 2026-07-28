using System.IO;
using System.Xml.Linq;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Guards for <c>docs/STANDALONE_NEW_SYSTEM_HOST.md</c> slice 1 — standalone host must not
/// open legacy Secret Setup or depend on SiNetSQL / V2 project references.
/// </summary>
public sealed class StandaloneNewSystemHostBoundaryTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate repo root (SiNet.sln).");
        }
    }

    private static string AppWpfCsprojPath =>
        Path.Combine(RepoRoot, "src", "SiNet.App.Wpf", "SiNet.App.Wpf.csproj");

    private static string AppXamlCsPath =>
        Path.Combine(RepoRoot, "src", "SiNet.App.Wpf", "App.xaml.cs");

    [Fact]
    public void App_Wpf_csproj_does_not_reference_SiNetSQL_or_V2()
    {
        var references = XDocument.Load(AppWpfCsprojPath)
            .Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(
            references,
            r => r.Contains("SiNetSQL", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            references,
            r => r.Contains("SiNetProjectManagerV2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void App_startup_opens_NewShell_via_INewShellFactory()
    {
        var source = File.ReadAllText(AppXamlCsPath);
        Assert.Contains("INewShellFactory", source, StringComparison.Ordinal);
        Assert.Contains("CreateShellAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<MainWindow>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_startup_does_not_open_legacy_SecretSetupWindow()
    {
        var source = File.ReadAllText(AppXamlCsPath);
        Assert.DoesNotContain("WPF_Window.SecretSetupWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SecretSetupWindow()", source, StringComparison.Ordinal);
        Assert.Contains("SecretSetupWindow", source, StringComparison.Ordinal);
        Assert.Contains("AddSiNetVaultBootstrap", source, StringComparison.Ordinal);
    }

    [Fact]
    public void V2_NewSystem_path_logs_deprecation()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot, "SiNetProjectManagerV2", "App.xaml.cs"));
        Assert.Contains("DEPRECATED", source, StringComparison.Ordinal);
        Assert.Contains("STANDALONE_NEW_SYSTEM_HOST.md", source, StringComparison.Ordinal);
    }
}
