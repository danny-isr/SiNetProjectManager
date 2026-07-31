using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.Surfaces.Workflow;
using SiNet.Application.Identity;
using SiNet.Application.ProjectWork;
using SiNet.Application.WorkSurfaces;
using Xunit;

namespace SiNet.App.Wpf.Tests.Shell;

/// <summary>
/// Release menu gating for standalone pilot surfaces. Offline — no DB/Gmail/ACC.
/// See <c>docs/TEST_STRATEGY.md</c> L3.
/// </summary>
public sealed class NewShellReleaseMenuGatingTests
{
    [Theory]
    [InlineData("מיילים", AppFeatureCodes.ShellOpenEmailSurface)]
    [InlineData("בעבודה 2", AppFeatureCodes.ShellOpenProjectWorkSurface)]
    [InlineData("לוח משימות", AppFeatureCodes.ShellOpenTaskPanelReadOnly)]
    [InlineData("דוחות ביקורת", AppFeatureCodes.ShellOpenInspectionSurface)]
    [InlineData("צפייה בתהליכים (סגור)", AppFeatureCodes.ShellOpenWorkflowClosedViewer)]
    [InlineData("R01 — סיכום שעות", AppFeatureCodes.ReportsManagement)]
    [InlineData("R02 — שעות עבודה", AppFeatureCodes.ReportsManagement)]
    [InlineData("R03 — השוואת נוכחות", AppFeatureCodes.ReportsManagement)]
    [InlineData("מפתחות וסודות", AppFeatureCodes.SystemSettingsWrite)]
    [InlineData("הגדרות מערכת", AppFeatureCodes.SystemSettingsWrite)]
    [InlineData("מיפוי MasterPlan", AppFeatureCodes.SystemSettingsWrite)]
    [InlineData("סטטוס ACC", AppFeatureCodes.SystemSettingsWrite)]
    [InlineData("ניהול קבצים", AppFeatureCodes.ShellOpenFileCatalogAdmin)]
    [InlineData("בריאות תהליכים", AppFeatureCodes.ShellOpenWorkflowOpsDashboard)]
    public void WhenFeatureGrantedThenMenuItemIsVisible(string title, string featureCode)
    {
        var items = BuildFlattened(granted: [featureCode], authenticated: true);
        Assert.Contains(items, i => i.Title == title && i.IsAvailable);
    }

    [Theory]
    [InlineData("מיילים")]
    [InlineData("בעבודה 2")]
    [InlineData("לוח משימות")]
    [InlineData("דוחות ביקורת")]
    [InlineData("צפייה בתהליכים (סגור)")]
    [InlineData("R01 — סיכום שעות")]
    [InlineData("מפתחות וסודות")]
    [InlineData("מיפוי MasterPlan")]
    [InlineData("ניהול קבצים")]
    [InlineData("בריאות תהליכים")]
    public void WhenNoFeaturesGrantedThenGatedMenuItemsAreHidden(string title)
    {
        var items = BuildFlattened(granted: [], authenticated: true);
        Assert.DoesNotContain(items, i => i.Title == title);
    }

    [Fact]
    public void WhenAuthenticatedThenSystemStatusAndPersonalSettingsVisible()
    {
        var items = BuildFlattened(granted: [], authenticated: true);
        Assert.Contains(items, i => i.Title == "מצב מערכת" && i.IsAvailable);
        Assert.Contains(items, i => i.Title == "הגדרות אישיות" && i.IsAvailable);
    }

    [Fact]
    public void WhenNotAuthenticatedThenSystemStatusAndPersonalSettingsHidden()
    {
        var items = BuildFlattened(granted: [AppFeatureCodes.SystemSettingsWrite], authenticated: false);
        Assert.DoesNotContain(items, i => i.Title == "מצב מערכת");
        Assert.DoesNotContain(items, i => i.Title == "הגדרות אישיות");
    }

    [Fact]
    public void DebugHarnessAndDevToolsAreWrappedInDebugPreprocessor()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");

        Assert.Contains("ביקורת (מעטפת — DEBUG)", source, StringComparison.Ordinal);
        Assert.Contains("כלי פיתוח", source, StringComparison.Ordinal);

        AssertInsideDebugBlock(source, "ביקורת (מעטפת — DEBUG)");
        AssertInsideDebugBlock(source, "כלי פיתוח");
    }

    private static void AssertInsideDebugBlock(string source, string marker)
    {
        var idx = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Missing marker: {marker}");
        var preceding = source[..idx];
        var lastDebug = preceding.LastIndexOf("#if DEBUG", StringComparison.Ordinal);
        var lastEndif = preceding.LastIndexOf("#endif", StringComparison.Ordinal);
        Assert.True(lastDebug > lastEndif, $"{marker} must be inside #if DEBUG");
    }

    private static IReadOnlyList<NewShellMenuItem> BuildFlattened(
        IReadOnlyCollection<string> granted,
        bool authenticated)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationQueryService>(new StubAuthorization(granted));
        services.AddSingleton<ICurrentUserContext>(new StubUserContext(authenticated ? 7 : null));

        // NewShellFactory gates these behind GetService + feature code.
        // ProjectWork menu resolves the concrete ProjectWorkSurfaceHost type (not the interface).
        services.AddSingleton<IEmailSurfaceHost, StubEmailHost>();
        services.AddSingleton<IShellContentHost, ShellContentHost>();
        services.AddSingleton<IProjectWorkWindowFactory, StubProjectWorkWindowFactory>();
        services.AddSingleton<ProjectWorkTaskFloatingHost>();
        services.AddSingleton<ProjectWorkSurfaceHost>();
        services.AddSingleton<ITaskPanelReadOnlyWindowFactory, StubTaskPanelFactory>();
        services.AddSingleton<IInspectionWindowFactory, StubInspectionFactory>();
        services.AddSingleton<IWorkflowClosedViewerWindowFactory, StubWorkflowFactory>();
        services.AddSingleton<IProjectCreateDialogFactory, StubProjectCreateFactory>();

        var sp = services.BuildServiceProvider();
        var factory = new NewShellFactory(sp);
        return NewShellMenuReflection.BuildFlattened(factory);
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

    private sealed class StubAuthorization(IReadOnlyCollection<string> granted) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> CanCurrentUserAccessFeatureAsync(string featureCode, CancellationToken cancellationToken = default)
            => Task.FromResult(granted.Contains(featureCode));
    }

    private sealed class StubUserContext(int? userId) : ICurrentUserContext
    {
        public int? UserId { get; } = userId;
    }

    private sealed class StubEmailHost : IEmailSurfaceHost
    {
        public void Show(WorkSurfaceContext? context = null) { }
        public EmailWindowViewModel? TryGetViewModel() => null;
        public bool TryBlockShellClose(Window owner) => false;
    }

    private sealed class StubProjectWorkWindowFactory : IProjectWorkWindowFactory
    {
        public ProjectWorkWindowView Create() => throw new InvalidOperationException("not used");
    }

    private sealed class StubTaskPanelFactory : ITaskPanelReadOnlyWindowFactory
    {
        public TaskWorkbenchView Create() => throw new InvalidOperationException("not used");
        public TaskWorkbenchView ShowOrActivate() => throw new InvalidOperationException("not used");
    }

    private sealed class StubInspectionFactory : IInspectionWindowFactory
    {
        public InspectionWindowView Create() => throw new InvalidOperationException("not used");
    }

    private sealed class StubWorkflowFactory : IWorkflowClosedViewerWindowFactory
    {
        public Window Create() => throw new InvalidOperationException("not used");
    }

    private sealed class StubProjectCreateFactory : IProjectCreateDialogFactory
    {
        public ProjectCreateDialogResult ShowDialog(Window? owner) => new(false);
        public ProjectCreateDialogResult ShowDialog(Window? owner, int? emailMessageId) => new(false);
    }
}
