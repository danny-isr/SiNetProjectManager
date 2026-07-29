using System.Collections.Concurrent;
using Moq;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.App.Wpf.Autodesk;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin;

/// <summary>
/// Guards the UI-thread affinity of <see cref="SettingsViewModel"/> permission refresh.
/// <para>
/// Regression: <c>RefreshPermissionFlagsAsync</c> awaited the authorization query with
/// <c>ConfigureAwait(false)</c> and then assigned WPF-bound properties and raised
/// <c>CanExecuteChanged</c>. Whenever the query really went async instead of returning a cached
/// result, the continuation landed on the thread pool and WPF threw
/// "The calling thread cannot access this object because a different thread owns it", which
/// <c>LoadAsync</c> swallowed into the summary banner — leaving settings unloaded and the theme
/// unapplied.
/// </para>
/// </summary>
public sealed class SettingsViewModelThreadAffinityTests
{
    [Fact]
    public void WhenAuthorizationCompletesAsynchronouslyThenPermissionFlagsStayOnTheUiThread()
    {
        int? flagThreadId = null;

        var uiThreadId = SingleThreadContext.Run(async () =>
        {
            var viewModel = CreateViewModelWithAsyncAuthorization();
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SettingsViewModel.CanViewPersonalSettings))
                {
                    flagThreadId ??= Environment.CurrentManagedThreadId;
                }
            };

            await viewModel.RefreshPermissionFlagsAsync();
        });

        Assert.NotNull(flagThreadId);
        Assert.Equal(uiThreadId, flagThreadId);
    }

    /// <summary>
    /// Authorization that only completes after a real thread hop, mirroring a live DB round-trip.
    /// A <c>Task.FromResult</c> stub resumes inline and cannot reproduce the regression.
    /// </summary>
    private static SettingsViewModel CreateViewModelWithAsyncAuthorization()
    {
        var authorization = new Mock<IAuthorizationQueryService>();
        authorization
            .Setup(a => a.CanCurrentUserAccessFeatureAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(20).ConfigureAwait(false);
                return false;
            });

        var accModeProvider = new Mock<IAccServiceModeProvider>();
        accModeProvider.SetupGet(x => x.Mode).Returns(AccServiceMode.Local);
        accModeProvider.SetupGet(x => x.BaseUrl).Returns((string?)null);

        var accKeyDiagnostics = new Mock<IAccServiceKeyDiagnostics>();
        accKeyDiagnostics.Setup(x => x.Describe()).Returns(new AccServiceKeyInfo(false, 0, null));

        return new SettingsViewModel(
            Mock.Of<IAppSettingsService>(),
            Mock.Of<ISystemSettingsQueryService>(),
            Mock.Of<ISystemSettingsCommandService>(),
            Mock.Of<ILoggingSettingsCommandService>(),
            Mock.Of<ILoggingRuntimeApplier>(),
            Mock.Of<IThemeRuntimeApplier>(),
            Mock.Of<IStatusColorSettingsService>(),
            new AccControlPlaneStatusPresenter(
                accModeProvider.Object,
                Mock.Of<IAccProjectService>(),
                accKeyDiagnostics.Object,
                Mock.Of<IAccServiceHealthProbe>(),
                Mock.Of<IAccServiceDiagnosticsProbe>()),
            Mock.Of<IAccProjectCatalogService>(),
            Mock.Of<IAccDocumentService>(),
            Mock.Of<IAccFolderBrowserService>(),
            Mock.Of<IAccProjectTreeSearchService>(),
            Mock.Of<IAccLiveProjectDiscoveryService>(),
            Mock.Of<IAccResolvedDocsUrlLauncher>(),
            Mock.Of<IClipboardTextWriter>(),
            authorization.Object,
            new StubCurrentUser(1),
            SettingsSurfaceScope.Personal);
    }

    private sealed class StubCurrentUser(int userId) : ICurrentUserContext
    {
        public int? UserId { get; } = userId;
    }

    /// <summary>
    /// Minimal stand-in for the WPF dispatcher: continuations posted to it are pumped back on the
    /// single thread that owns the context, so a lost context shows up as a thread-id mismatch.
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        /// <summary>Pumps <paramref name="body"/> to completion and returns the owning thread id.</summary>
        public static int Run(Func<Task> body)
        {
            var previous = Current;
            var context = new SingleThreadContext();
            SetSynchronizationContext(context);
            try
            {
                var task = body();
                _ = task.ContinueWith(
                    _ => context._queue.CompleteAdding(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
                {
                    callback(state);
                }

                task.GetAwaiter().GetResult();
                return Environment.CurrentManagedThreadId;
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }
}
