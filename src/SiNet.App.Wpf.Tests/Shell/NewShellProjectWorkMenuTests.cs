using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.Identity;
using SiNet.Application.ProjectWork;
using SiNet.Application.WorkSurfaces;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

/// <summary>
/// Wave 1 guardrails: NewShell menu exposes «בעבודה 2» under projects (after email),
/// and the surface host caches a single view across navigations.
/// </summary>
public sealed class NewShellProjectWorkMenuTests
{
    [Fact]
    public void NewShell_shows_project_work_menu_when_authorized()
    {
        var top = BuildMenuItems(projectWorkAuthorized: true, emailAuthorized: true);
        var projects = Assert.Single(top, g => g.Title == "פרויקטים ותבניות");
        var titles = projects.Children.Select(c => c.Title).ToList();

        Assert.Contains("מיילים", titles);
        Assert.Contains("בעבודה 2", titles);
        Assert.True(titles.IndexOf("מיילים") < titles.IndexOf("בעבודה 2"));
    }

    [Fact]
    public void NewShell_hides_project_work_menu_when_feature_denied()
    {
        var items = Flatten(BuildMenuItems(projectWorkAuthorized: false, emailAuthorized: true));
        Assert.DoesNotContain(items, i => i.Title == "בעבודה 2");
        Assert.Contains(items, i => i.Title == "מיילים");
    }

    [Fact]
    public void NewShellFactory_opens_project_work_via_surface_host_not_legacy_window()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.Contains("ProjectWorkSurfaceHost", source, StringComparison.Ordinal);
        Assert.Contains("בעבודה 2", source, StringComparison.Ordinal);
        Assert.Contains("ShellOpenProjectWorkSurface", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectWorkView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowNativeProjectWork", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectWorkSurfaceHost_returns_false_when_shell_not_attached()
    {
        await RunStaAsync(async () =>
        {
            var services = new ServiceCollection();
            services.AddSingleton<IShellContentHost, ShellContentHost>();
            services.AddSingleton<IProjectWorkWindowFactory>(new StubFactory());
            services.AddSingleton<ProjectWorkTaskFloatingHost>();
            var sp = services.BuildServiceProvider();
            var host = new ProjectWorkSurfaceHost(
                sp,
                sp.GetRequiredService<IShellContentHost>(),
                sp.GetRequiredService<ProjectWorkTaskFloatingHost>());

            Assert.False(await host.TryOpenBrowseAsync());
        });
    }

    [Fact]
    public async Task ProjectWorkSurfaceHost_reuses_cached_view_across_navigations()
    {
        await RunStaAsync(async () =>
        {
            var contentHost = new ShellContentHost();
            var shellVm = new NewShellViewModel([], currentUserDisplay: "test");
            contentHost.Attach(shellVm);

            var factory = new RecordingFactory();
            var services = new ServiceCollection();
            services.AddSingleton<IShellContentHost>(contentHost);
            services.AddSingleton<IProjectWorkWindowFactory>(factory);
            services.AddSingleton<ProjectWorkTaskFloatingHost>();
            var sp = services.BuildServiceProvider();
            var host = new ProjectWorkSurfaceHost(
                sp,
                contentHost,
                sp.GetRequiredService<ProjectWorkTaskFloatingHost>());

            Assert.True(await host.TryOpenBrowseAsync());
            var first = shellVm.CurrentContent;
            Assert.NotNull(first);
            Assert.Equal(1, factory.CreateCount);

            Assert.True(await host.TryOpenBrowseAsync());
            Assert.Same(first, shellVm.CurrentContent);
            Assert.Equal(1, factory.CreateCount);
        });
    }

    private static Task RunStaAsync(Func<Task> body)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                body().GetAwaiter().GetResult();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    [Fact]
    public void ActiveFileQueryHub_UnregisterProvider_only_clears_when_same_instance()
    {
        var hub = new ActiveFileQueryHub();
        var a = new StubActiveFiles();
        var b = new StubActiveFiles();

        hub.RegisterProvider(a);
        hub.UnregisterProvider(b);
        Assert.True(hub.IsAvailable);

        hub.UnregisterProvider(a);
        Assert.False(hub.IsAvailable);
    }

    private static IReadOnlyList<NewShellMenuItem> BuildMenuItems(bool projectWorkAuthorized, bool emailAuthorized)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationQueryService>(
            new StubAuthorization(projectWorkAuthorized, emailAuthorized));
        services.AddSingleton<IShellContentHost, ShellContentHost>();
        services.AddSingleton<IProjectWorkWindowFactory>(new StubFactory());
        services.AddSingleton<ProjectWorkTaskFloatingHost>();
        services.AddSingleton<ProjectWorkSurfaceHost>();
        // Email menu only needs the host type present; Show is never invoked in these tests.
        services.AddSingleton<SiNet.App.Wpf.Surfaces.Email.IEmailSurfaceHost, StubEmailHost>();

        var sp = services.BuildServiceProvider();
        var factory = new NewShellFactory(sp);
        var method = typeof(NewShellFactory).GetMethod(
            "BuildMigratedOnlyMenu",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (IReadOnlyList<NewShellMenuItem>)method!.Invoke(factory, null)!;
    }

    private static IEnumerable<NewShellMenuItem> Flatten(IEnumerable<NewShellMenuItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children))
                yield return child;
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private sealed class StubAuthorization(bool projectWork, bool email) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default)
            => Task.FromResult(requiredRole == AppRole.Employee);

        public Task<bool> CanCurrentUserAccessFeatureAsync(string featureCode, CancellationToken cancellationToken = default)
            => Task.FromResult(
                (projectWork && featureCode == AppFeatureCodes.ShellOpenProjectWorkSurface)
                || (email && featureCode == AppFeatureCodes.ShellOpenEmailSurface));
    }

    private sealed class StubEmailHost : SiNet.App.Wpf.Surfaces.Email.IEmailSurfaceHost
    {
        public void Show(WorkSurfaceContext? context = null) { }
        public SiNet.App.Wpf.Surfaces.Email.EmailWindowViewModel? TryGetViewModel() => null;
        public bool TryBlockShellClose(System.Windows.Window owner) => false;
    }

    private sealed class StubFactory : IProjectWorkWindowFactory
    {
        public ProjectWorkWindowView Create() => new(new ProjectWorkWindowViewModel());
    }

    private sealed class RecordingFactory : IProjectWorkWindowFactory
    {
        public int CreateCount { get; private set; }

        public ProjectWorkWindowView Create()
        {
            CreateCount++;
            return new ProjectWorkWindowView(new ProjectWorkWindowViewModel());
        }
    }

    private sealed class StubActiveFiles : IActiveFileQueryService
    {
        public bool IsAvailable => true;
        public int? CurrentProjectNumber => 1;

        public IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(int folderId) => [];
        public IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(string folderFullPath) => [];
        public IReadOnlyList<ActiveFileInfo> GetActiveFilesInFolder(int folderId, bool recursive) => [];
    }
}
