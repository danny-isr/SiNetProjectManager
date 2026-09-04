using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Source guards for standalone host startup order in <c>App.xaml.cs</c>.
/// See <c>docs/TEST_STRATEGY.md</c> L3 and <c>docs/STANDALONE_NEW_SYSTEM_HOST.md</c>.
/// </summary>
public sealed class StandaloneStartupSequenceTests
{
    [Fact]
    public void App_startup_orders_vault_then_composition_then_schema_then_auth_then_shell()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/App.xaml.cs");

        var vault = IndexOf(source, "EnsureVaultDatabaseReadyAsync");
        var compose = IndexOf(source, "AddSiNetStandaloneHost");
        var schema = IndexOf(source, "ValidateSchemaAsync");
        var auth = IndexOf(source, "AuthenticateAsync");
        var shell = IndexOf(source, "CreateShellAsync");

        Assert.True(vault < compose, "Vault gate must precede AddSiNetStandaloneHost");
        Assert.True(compose < schema, "Composition must precede schema gate");
        Assert.True(schema < auth, "Schema gate must precede Windows-user auth");
        Assert.True(auth < shell, "Auth must precede CreateShellAsync");
        Assert.Contains("WindowsUserAuthStatus.Blocked", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_startup_restores_gmail_silently_without_interactive_login()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/App.xaml.cs");

        Assert.Contains("TryRestoreSessionAsync", source, StringComparison.Ordinal);
        Assert.Contains("StartConnectorAuthRestore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoginAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SignInInteractiveAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_startup_does_not_open_legacy_secret_or_main_window()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/App.xaml.cs");

        Assert.DoesNotContain("WPF_Window.SecretSetupWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SecretSetupWindow()", source, StringComparison.Ordinal);
        Assert.Contains("AddSiNetVaultBootstrap", source, StringComparison.Ordinal);
        Assert.Contains("SecretSetupWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<MainWindow>", source, StringComparison.Ordinal);
        Assert.Contains("INewShellFactory", source, StringComparison.Ordinal);
    }

    private static int IndexOf(string source, string marker)
    {
        var idx = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Missing marker in App.xaml.cs: {marker}");
        return idx;
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
