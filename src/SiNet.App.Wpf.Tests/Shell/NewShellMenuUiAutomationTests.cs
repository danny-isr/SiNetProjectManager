using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

/// <summary>
/// Guards shell menu UI Automation metadata so WpfPilot / FlaUI can select MenuItems
/// by Hebrew Title rather than the CLR type name <c>NewShellMenuItem</c>.
/// </summary>
public sealed class NewShellMenuUiAutomationTests
{
    [Fact]
    public void NewShell_menu_item_container_binds_AutomationProperties_Name_to_Title()
    {
        var xaml = File.ReadAllText(NewShellWindowXamlPath);

        Assert.Contains(
            "Property=\"AutomationProperties.Name\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Value=\"{Binding Title}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NewShell_menu_item_container_binds_AutomationProperties_HelpText_to_Description()
    {
        var xaml = File.ReadAllText(NewShellWindowXamlPath);

        Assert.Contains(
            "Property=\"AutomationProperties.HelpText\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Value=\"{Binding Description}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NewShell_menu_item_container_style_keeps_theme_BasedOn_and_command_binding()
    {
        var xaml = File.ReadAllText(NewShellWindowXamlPath);

        Assert.Contains(
            "BasedOn=\"{StaticResource {x:Type MenuItem}}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Property=\"Command\" Value=\"{Binding OpenCommand}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellMenuItem_open_marshals_to_wpf_dispatcher_for_uia_invoke()
    {
        var source = File.ReadAllText(NewShellMenuItemPath);

        Assert.Contains("dispatcher.CheckAccess()", source, StringComparison.Ordinal);
        Assert.Contains("dispatcher.Invoke(_open)", source, StringComparison.Ordinal);
        Assert.Contains("InvokeOpen()", source, StringComparison.Ordinal);
    }

    private static string NewShellWindowXamlPath =>
        Path.Combine(Boundary.RepoPaths.RepoRoot, "src", "SiNet.App.Wpf", "Shell", "NewShellWindow.xaml");

    private static string NewShellMenuItemPath =>
        Path.Combine(Boundary.RepoPaths.RepoRoot, "src", "SiNet.App.Wpf", "Shell", "NewShellMenuItem.cs");
}
