using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Admin.SystemStatus;
using SiNet.App.Wpf.Runtime;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using SiNet.Application.Runtime;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

public sealed class NewShellSystemStatusMenuTests
{
    [Fact]
    public void NewShell_shows_system_status_menu_for_authenticated_user()
    {
        var items = BuildMenuItems(authenticated: true);

        Assert.Contains(items, i => i.Title == "מצב מערכת" && i.IsAvailable);
    }

    [Fact]
    public void NewShell_hides_system_status_menu_when_not_authenticated()
    {
        var items = BuildMenuItems(authenticated: false);

        Assert.DoesNotContain(items, i => i.Title == "מצב מערכת");
    }

    [Fact]
    public void NewShellFactory_opens_native_system_status_window_not_legacy()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("SystemStatusWindow", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeSystemStatus", source, StringComparison.Ordinal);
        Assert.Contains("מצב מערכת", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemHealthWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ISystemHealthService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellViewModel_footer_shows_background_work_from_runtime_status()
    {
        var runtime = new StubRuntime(
        [
            new SubsystemRuntimeStatus(
                "database", "מסד נתונים", SubsystemRuntimeState.Idle, null, "תקין", DateTimeOffset.UtcNow),
            new SubsystemRuntimeStatus(
                "acc-ingest", "העלאות ACC", SubsystemRuntimeState.Running, 3, "פעיל", DateTimeOffset.UtcNow),
        ]);

        using var vm = new NewShellViewModel(
            [],
            currentUserDisplay: "בדיקה",
            runtimeStatus: runtime,
            openSystemStatus: static () => { });

        Assert.Equal(3, vm.ActiveBackgroundWorkCount);
        Assert.Contains("רקע פעיל", vm.StatusText, StringComparison.Ordinal);
        Assert.True(vm.CanOpenSystemStatus);
    }

    private static IReadOnlyList<NewShellMenuItem> BuildMenuItems(bool authenticated)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserContext>(new StubUserContext(authenticated ? 1 : null));
        services.AddSingleton<IStartupTaskRegistry, StartupTaskRegistry>();
        services.AddSingleton<IRuntimeSubsystemStatusService>(sp =>
            new RuntimeSubsystemStatusService(sp.GetRequiredService<IStartupTaskRegistry>()));
        services.AddTransient<SystemStatusViewModel>();
        services.AddTransient<SystemStatusWindow>();

        var sp = services.BuildServiceProvider();
        var factory = new NewShellFactory(sp);
        var method = typeof(NewShellFactory).GetMethod("BuildMigratedOnlyMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (IReadOnlyList<NewShellMenuItem>)method!.Invoke(factory, null)!;
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

    private sealed class StubRuntime(IReadOnlyList<SubsystemRuntimeStatus> current) : IRuntimeSubsystemStatusService
    {
        public IReadOnlyList<SubsystemRuntimeStatus> Current { get; } = current;
        public event EventHandler? Changed;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
