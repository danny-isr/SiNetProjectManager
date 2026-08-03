using System.IO;
using System.Reflection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

public sealed class NewShellWindowTitleTests
{
    [Fact]
    public void Base_title_appends_assembly_informational_version()
    {
        var expectedVersion = ReadWpfInformationalVersion();

        Assert.Equal($"{NewShellWindowTitle.BrandTitle} — {expectedVersion}", NewShellWindowTitle.BaseTitle);
        Assert.Equal(expectedVersion, NewShellWindowTitle.ResolveAppVersion());
    }

    [Fact]
    public void No_project_uses_base_window_title()
    {
        Assert.Equal(NewShellWindowTitle.BaseTitle, NewShellWindowTitle.Format(null));
    }

    [Fact]
    public void Project_with_number_and_name_includes_both_in_order()
    {
        var title = NewShellWindowTitle.Format(Project(1042, "1042", "מגדל השחר"));

        Assert.Equal($"{NewShellWindowTitle.BaseTitle} — 1042 — מגדל השחר", title);
    }

    [Fact]
    public void Project_with_name_only_omits_extra_separators()
    {
        var title = NewShellWindowTitle.Format(Project(1, "", "  Alpha  "));

        Assert.Equal($"{NewShellWindowTitle.BaseTitle} — Alpha", title);
    }

    [Fact]
    public void Project_with_number_only_omits_extra_separators()
    {
        var title = NewShellWindowTitle.Format(Project(2, " 5678 ", ""));

        Assert.Equal($"{NewShellWindowTitle.BaseTitle} — 5678", title);
    }

    [Fact]
    public async Task Changing_project_updates_window_title()
    {
        var context = new InMemoryCurrentProjectContext();
        var vm = CreateViewModel(context);

        await context.SetCurrentProjectAsync(Project(1, "1001", "Alpha"));
        Assert.Equal($"{NewShellWindowTitle.BaseTitle} — 1001 — Alpha", vm.WindowTitle);

        await context.SetCurrentProjectAsync(Project(2, "2002", "Beta"));
        Assert.Equal($"{NewShellWindowTitle.BaseTitle} — 2002 — Beta", vm.WindowTitle);
    }

    [Fact]
    public async Task Clearing_project_restores_base_window_title()
    {
        var context = new InMemoryCurrentProjectContext();
        var vm = CreateViewModel(context);

        await context.SetCurrentProjectAsync(Project(1, "1001", "Alpha"));
        await context.SetCurrentProjectAsync(null);

        Assert.Equal(NewShellWindowTitle.BaseTitle, vm.WindowTitle);
    }

    [Fact]
    public void NewShellWindow_code_behind_does_not_format_project_title()
    {
        var source = File.ReadAllText(Path.Combine(AppWpfRoot, "Shell", "NewShellWindow.xaml.cs"));

        Assert.DoesNotContain("SetCurrentProjectDisplay", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectNumber", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NewShellWindowTitle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShell_window_xaml_binds_os_title_to_window_title()
    {
        var xaml = File.ReadAllText(Path.Combine(AppWpfRoot, "Shell", "NewShellWindow.xaml"));
        Assert.Contains("Title=\"{Binding WindowTitle}\"", xaml, StringComparison.Ordinal);
    }

    private static NewShellViewModel CreateViewModel(ICurrentProjectContext context) =>
        new([], "Test User", context);

    private static ProjectSummaryDto Project(int id, string number, string name) =>
        new(id, number, name, null, null, null, null, null, true);

    private static string AppWpfRoot =>
        Path.Combine(Boundary.RepoPaths.RepoRoot, "src", "SiNet.App.Wpf");

    private static string ReadWpfInformationalVersion()
    {
        var asm = typeof(NewShellWindowTitle).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.False(string.IsNullOrWhiteSpace(info));

        var plus = info!.IndexOf('+');
        return plus > 0 ? info[..plus] : info;
    }
}
