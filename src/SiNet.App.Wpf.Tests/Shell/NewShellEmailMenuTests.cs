using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Identity;
using SiNet.Application.WorkSurfaces;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

/// <summary>
/// Wave A guardrails for Email Manual QA resume: NewShell menu, surface host cache, close wiring.
/// </summary>
public sealed class NewShellEmailMenuTests
{
    [Fact]
    public void NewShell_shows_email_menu_when_authorized()
    {
        var top = BuildMenuItems(emailAuthorized: true);
        var projects = Assert.Single(top, g => g.Title == "פרויקטים ותבניות");
        Assert.Contains(projects.Children, c => c.Title == "מיילים" && c.IsAvailable);
    }

    [Fact]
    public void NewShell_hides_email_menu_when_feature_denied()
    {
        var items = Flatten(BuildMenuItems(emailAuthorized: false));
        Assert.DoesNotContain(items, i => i.Title == "מיילים");
    }

    [Fact]
    public void NewShellFactory_opens_email_via_surface_host()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.Contains("IEmailSurfaceHost", source, StringComparison.Ordinal);
        Assert.Contains("מיילים", source, StringComparison.Ordinal);
        Assert.Contains("ShellOpenEmailSurface", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellWindow_blocks_close_via_email_surface_host()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellWindow.xaml.cs");
        Assert.Contains("TryBlockShellClose", source, StringComparison.Ordinal);
        Assert.Contains("IEmailSurfaceHost", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailSurfaceHost_cache_semantics_reuse_single_content_instance()
    {
        var contentHost = new ShellContentHost();
        var shellVm = new NewShellViewModel([], currentUserDisplay: "test");
        contentHost.Attach(shellVm);

        var recording = new RecordingEmailSurfaceHost(contentHost);
        recording.Show();
        var first = shellVm.CurrentContent;
        recording.Show();
        Assert.Same(first, shellVm.CurrentContent);
        Assert.Equal(1, recording.CreateCount);
    }

    private static IReadOnlyList<NewShellMenuItem> BuildMenuItems(bool emailAuthorized)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationQueryService>(new StubAuthorization(emailAuthorized));
        services.AddSingleton<IShellContentHost, ShellContentHost>();
        services.AddSingleton<IEmailSurfaceHost, StubEmailHost>();

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

    private sealed class StubAuthorization(bool email) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default)
            => Task.FromResult(requiredRole == AppRole.Employee);

        public Task<bool> CanCurrentUserAccessFeatureAsync(string featureCode, CancellationToken cancellationToken = default)
            => Task.FromResult(email && featureCode == AppFeatureCodes.ShellOpenEmailSurface);
    }

    private sealed class StubEmailHost : IEmailSurfaceHost
    {
        public void Show(WorkSurfaceContext? context = null) { }
        public EmailWindowViewModel? TryGetViewModel() => null;
        public bool TryBlockShellClose(System.Windows.Window owner) => false;
    }

    /// <summary>Mirrors EmailSurfaceHost cache semantics without constructing the full WPF view.</summary>
    private sealed class RecordingEmailSurfaceHost(IShellContentHost contentHost) : IEmailSurfaceHost
    {
        private object? _view;
        public int CreateCount { get; private set; }

        public void Show(WorkSurfaceContext? context = null)
        {
            if (_view is null)
            {
                CreateCount++;
                _view = new object();
            }

            contentHost.NavigateTo(_view);
        }

        public EmailWindowViewModel? TryGetViewModel() => null;
        public bool TryBlockShellClose(System.Windows.Window owner) => false;
    }
}
