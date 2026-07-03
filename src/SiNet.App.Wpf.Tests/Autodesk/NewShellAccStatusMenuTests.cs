using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

public sealed class NewShellAccStatusMenuTests
{
    [Fact]
    public void NewShell_shows_acc_status_menu_only_for_system_settings_admin()
    {
        var items = BuildMenuItems(systemSettingsWrite: true);

        Assert.Contains(items, i => i.Title == "סטטוס ACC" && i.IsAvailable);
    }

    [Fact]
    public void NewShell_hides_acc_status_menu_when_system_settings_denied()
    {
        var items = BuildMenuItems(systemSettingsWrite: false);

        Assert.DoesNotContain(items, i => i.Title == "סטטוס ACC");
    }

    [Fact]
    public void NewShellFactory_opens_native_acc_status_window_not_legacy()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("AccControlPlaneStatusWindow", source, StringComparison.Ordinal);
        Assert.Contains("OpenNativeAccControlPlaneStatus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagementSettingsWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetProjectManagerV2.WPF_Window", source, StringComparison.Ordinal);
    }

    private static IReadOnlyList<NewShellMenuItem> BuildMenuItems(bool systemSettingsWrite)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationQueryService>(new StubAuthorization(systemSettingsWrite));
        services.AddTransient(_ => new AccControlPlaneStatusWindow(
            new AccControlPlaneStatusWindowViewModel(
                new AccControlPlaneStatusPresenter(
                    new StubAccModeProvider(),
                    new StubAccKeyDiagnostics(),
                    new StubAccHealthProbe(),
                    new StubAccDiagnosticsProbe()))));

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

    private sealed class StubAuthorization(bool systemSettingsWrite) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default)
            => Task.FromResult(systemSettingsWrite && requiredRole == AppRole.Administrator);

        public Task<bool> CanCurrentUserAccessFeatureAsync(string featureCode, CancellationToken cancellationToken = default)
            => Task.FromResult(systemSettingsWrite && featureCode == AppFeatureCodes.SystemSettingsWrite);
    }

    private sealed class StubAccModeProvider : IAccServiceModeProvider
    {
        public AccServiceMode Mode => AccServiceMode.Local;
        public string? BaseUrl => null;
    }

    private sealed class StubAccKeyDiagnostics : IAccServiceKeyDiagnostics
    {
        public AccServiceKeyInfo Describe() => new(false, 0, null);
    }

    private sealed class StubAccHealthProbe : IAccServiceHealthProbe
    {
        public Task<AccServiceHealthResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccServiceHealthResult(false, AccServiceHealthState.NotConfigured, null, "Not configured"));
    }

    private sealed class StubAccDiagnosticsProbe : IAccServiceDiagnosticsProbe
    {
        public Task<AccServiceDiagnosticsResult> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccServiceDiagnosticsResult(false, null, false, null, 0, null, false, "Not configured", false, "Not configured"));
    }
}
