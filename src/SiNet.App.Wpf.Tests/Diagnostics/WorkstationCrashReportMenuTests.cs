using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Tests.Shell;
using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Diagnostics;

/// <summary>
/// «דוח קריסות תחנה» is deliberately not gated by a feature code: a user whose machine keeps crashing
/// must be able to produce the report without admin rights (DEV-010).
/// </summary>
public sealed class WorkstationCrashReportMenuTests
{
    [Fact]
    public void WhenAUserIsSignedInThenTheCrashReportItemIsAvailable()
    {
        var items = Flatten(BuildMenuItems(authenticated: true));

        Assert.Contains(items, i => i.Title == "דוח קריסות תחנה" && i.IsAvailable);
    }

    [Fact]
    public void WhenNobodyIsSignedInThenTheCrashReportItemIsHidden()
    {
        var items = Flatten(BuildMenuItems(authenticated: false));

        Assert.DoesNotContain(items, i => i.Title == "דוח קריסות תחנה");
    }

    [Fact]
    public void WhenTheItemIsDeclaredThenItLivesInTheAdminGroup()
    {
        var top = BuildMenuItems(authenticated: true);

        var admin = top.Single(g => g.Title == "מנהלה");

        Assert.Contains(admin.Children, i => i.Title == "דוח קריסות תחנה");
    }

    [Fact]
    public void WhenOpeningTheItemThenTheNativeWindowIsResolved()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("OpenNativeWorkstationCrashReport", source, StringComparison.Ordinal);
        Assert.Contains("WorkstationCrashReportWindow", source, StringComparison.Ordinal);
    }

    private static IReadOnlyList<NewShellMenuItem> BuildMenuItems(bool authenticated)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserContext>(new StubUserContext(authenticated ? 1 : null));

        var sp = services.BuildServiceProvider();
        var factory = new NewShellFactory(sp);
        return NewShellMenuReflection.Build(factory);
    }

    private static IEnumerable<NewShellMenuItem> Flatten(IEnumerable<NewShellMenuItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children))
            {
                yield return child;
            }
        }
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

    private sealed class StubUserContext(int? userId) : ICurrentUserContext
    {
        public int? UserId { get; } = userId;
    }
}
