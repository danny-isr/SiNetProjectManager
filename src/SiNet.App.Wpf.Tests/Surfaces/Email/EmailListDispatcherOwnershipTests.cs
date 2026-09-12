using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Projects;
using SiNet.Domain.ValueObjects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Email;

[Collection(WpfDispatcherCollection.Name)]
public sealed class EmailListDispatcherOwnershipTests
{
    private const int RepeatCount = 20;

    [Fact]
    public Task CollectionView_rejects_background_mutation_without_marshal() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            var items = new ObservableCollection<string> { "a" };
            var view = CollectionViewSource.GetDefaultView(items);
            Assert.NotNull(view);

            Exception? thrown = null;
            await Task.Run(() =>
            {
                try
                {
                    items.Clear();
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            }).ConfigureAwait(true);

            Assert.IsType<NotSupportedException>(thrown);
            Assert.Contains("Dispatcher", thrown!.Message, StringComparison.Ordinal);
        });

    [Fact]
    public Task ReplaceRows_from_background_thread() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            for (var i = 0; i < RepeatCount; i++)
            {
                var sut = CreateList();
                var view = CollectionViewSource.GetDefaultView(sut.Emails);
                Assert.NotNull(view);
                var rows = new[]
                {
                    EmailListViewModelTestFixtures.CreateRow(1, isFiledToProject: false) with { Id = "bg-1" },
                    EmailListViewModelTestFixtures.CreateRow(2, isFiledToProject: false) with { Id = "bg-2" },
                };

                await Task.Run(() => sut.ReplaceRowsForTests(rows)).ConfigureAwait(true);

                Assert.Equal(2, sut.Emails.Count);
                Assert.Equal("bg-1", sut.Emails[0].Id);
                view.Refresh();
                Assert.Equal(2, view.Cast<EmailListRow>().Count());
            }
        });

    [Fact]
    public Task RefreshRowBackgrounds_from_background_thread() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            for (var i = 0; i < RepeatCount; i++)
            {
                var project = new EmailListViewModelTestFixtures.RaisingCurrentProjectContext();
                var sut = CreateList(project: project);
                sut.ReplaceRowsForTests(
                [
                    EmailListViewModelTestFixtures.CreateRow(10, isFiledToProject: true) with
                    {
                        Id = "filed-1",
                        ProjectId = 1042,
                    },
                ]);

                await Task.Run(() => project.Raise(EmailListViewModelTestFixtures.CreateProject()))
                    .ConfigureAwait(true);

                Assert.NotEmpty(sut.Emails);
                Assert.NotNull(CollectionViewSource.GetDefaultView(sut.Emails));
            }
        });

    [Fact]
    public Task AvailableLabels_background_refresh() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            var gateway = new LabelListGateway();
            var sut = CreateList(gateway);
            await Task.Run(() => sut.LoadLabelsForTestsAsync()).ConfigureAwait(true);

            Assert.Equal(2, sut.AvailableLabels.Count);
            Assert.Equal("Work", sut.AvailableLabels[0].Name);
            Assert.Equal("Clients", sut.AvailableLabels[1].Name);
        });

    [Fact]
    public Task Grouping_background_reload_rebuilds_display_collections() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            var gateway = new EmailListViewModelTestFixtures.LabelGroupingEmailGateway();
            var sut = CreateList(gateway);
            await sut.InitializeAsync().ConfigureAwait(true);
            await Task.Run(() => sut.RefreshPageAsync()).ConfigureAwait(true);

            Assert.NotEmpty(sut.Emails);
            Assert.True(sut.DisplayGroups.Count + sut.FlatDisplayEmails.Count > 0);
            foreach (var group in sut.DisplayGroups)
            {
                _ = group.Emails.Count;
            }

            CollectionViewSource.GetDefaultView(sut.Emails)!.Refresh();
        });

    [Fact]
    public Task Concurrent_initial_reload_serializes_and_both_callers_wait() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            for (var i = 0; i < RepeatCount; i++)
            {
                var gateway = new BarrierPagingGateway();
                var gate = new MailboxReloadOrchestrator();
                var sut = CreateList(gateway, reload: gate);
                sut.SearchText = "first-pass";

                var first = sut.RefreshPageAsync();
                await WaitUntilAsync(() => gateway.Calls >= 1);
                var second = sut.ClearFiltersAndReloadAsync();

                Assert.False(second.IsCompleted);
                Assert.True(gate.ReloadPending || gateway.Calls >= 2);

                gateway.ReleaseOne();
                await WaitUntilAsync(() => gateway.Calls >= 2);
                Assert.False(second.IsCompleted);
                gateway.ReleaseOne();

                await first.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await second.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

                Assert.True(string.IsNullOrEmpty(sut.SearchText));
                Assert.NotEmpty(sut.Emails);
                Assert.InRange(gateway.Calls, 2, 3);
                Assert.False(gate.IsBusy);
            }
        });

    [Fact]
    public Task Mailbox_reload_single_flight_external_waiter_sees_latest() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            for (var i = 0; i < RepeatCount; i++)
            {
                var gateway = new BarrierPagingGateway();
                var gate = new MailboxReloadOrchestrator();
                var sut = CreateList(gateway, reload: gate);

                var first = sut.RefreshPageAsync();
                await WaitUntilAsync(() => gateway.Calls >= 1);
                sut.SearchText = "latest";
                var second = sut.ApplyFiltersAsync();

                Assert.False(second.IsCompleted);
                gateway.ReleaseOne();
                await WaitUntilAsync(() => gateway.Calls >= 2);
                gateway.ReleaseOne();

                await first.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
                await second.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

                Assert.Equal("latest", sut.SearchText);
                Assert.Equal("latest", gateway.LastQuery?.FreeText);
                Assert.False(gate.IsBusy);
            }
        });

    [Fact]
    public Task Exception_observation_reports_and_sets_load_error() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            var reported = new List<(Exception Ex, string Context)>();
            void OnReported(Exception ex, string context) => reported.Add((ex, context));
            var unobservedBoom = 0;
            void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
            {
                if (e.Exception.InnerExceptions.Any(static x =>
                        x.Message.Contains("boom-unobserved", StringComparison.Ordinal)))
                {
                    Interlocked.Increment(ref unobservedBoom);
                }

                e.SetObserved();
            }

            AppErrorReporter.ExceptionReported += OnReported;
            TaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                await ObservedTask.Run(
                    async () =>
                    {
                        await Task.Yield();
                        throw new InvalidOperationException("boom-unobserved");
                    },
                    "Email.TestFault").ConfigureAwait(true);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(50).ConfigureAwait(true);

                Assert.Contains(reported, item => item.Context == "Email.TestFault");
                Assert.Equal(0, Volatile.Read(ref unobservedBoom));

                var sut = CreateList(new ThrowingPagingGateway());
                await sut.LoadMailboxAndProjectForTestsAsync(resetStack: true).ConfigureAwait(true);
                Assert.False(string.IsNullOrWhiteSpace(sut.LoadError));
                Assert.Contains("Gmail exploded", sut.LoadError, StringComparison.Ordinal);
            }
            finally
            {
                AppErrorReporter.ExceptionReported -= OnReported;
                TaskScheduler.UnobservedTaskException -= OnUnobserved;
            }
        });

    [Fact]
    public Task Already_on_ui_thread_reload_does_not_deadlock() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            var sut = CreateList();
            Assert.True(UiThread.Dispatcher!.CheckAccess());
            await sut.RefreshPageAsync().ConfigureAwait(true);
            Assert.True(UiThread.Dispatcher!.CheckAccess());
            Assert.NotEmpty(sut.Emails);
            Assert.False(sut.IsBusy);
        });

    [Fact]
    public Task History_reload_from_background_is_dispatcher_safe() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            var sut = CreateList();
            await sut.RefreshPageAsync().ConfigureAwait(true);
            await Task.Run(() => sut.RefreshPageCoreAsync()).ConfigureAwait(true);
            Assert.NotEmpty(sut.Emails);
            CollectionViewSource.GetDefaultView(sut.Emails)!.Refresh();
        });

    [Fact]
    public Task Current_project_background_event_is_dispatcher_safe() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            var project = new EmailListViewModelTestFixtures.RaisingCurrentProjectContext();
            var sut = CreateList(project: project);
            sut.ReplaceRowsForTests(
            [
                EmailListViewModelTestFixtures.CreateRow(3, isFiledToProject: true) with
                {
                    Id = "proj-row",
                    ProjectId = 99,
                },
            ]);

            await Task.Run(() => project.Raise(new ProjectSummaryDto(
                7, "7", "Other", "SI", null, null, null, null, true))).ConfigureAwait(true);

            Assert.Single(sut.Emails);
            CollectionViewSource.GetDefaultView(sut.Emails)!.Refresh();
        });

    [Fact]
    public Task Coalesced_follow_up_starts_off_dispatcher_and_notifies_on_dispatcher() =>
        WpfStaTestHost.RunAsync(async () =>
        {
            var gateway = new BarrierPagingGateway();
            var gate = new MailboxReloadOrchestrator();
            var sut = CreateList(gateway, reload: gate);
            var offDispatcher = new ConcurrentBag<string>();

            void Probe(object? sender, PropertyChangedEventArgs e)
            {
                var dispatcher = UiThread.Dispatcher;
                if (dispatcher is not null && !dispatcher.CheckAccess())
                {
                    offDispatcher.Add($"{sender?.GetType().Name}.{e.PropertyName}");
                }
            }

            void ProbeCanExecute(object? sender, EventArgs e)
            {
                var dispatcher = UiThread.Dispatcher;
                if (dispatcher is not null && !dispatcher.CheckAccess())
                {
                    offDispatcher.Add($"{sender?.GetType().Name}.CanExecuteChanged");
                }
            }

            sut.PropertyChanged += Probe;
            sut.DisplayGroups.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is not null)
                {
                    foreach (var item in args.NewItems)
                    {
                        if (item is EmailLabelGroupViewModel group)
                        {
                            group.PropertyChanged += Probe;
                        }
                    }
                }
            };
            ((ICommand)sut.LoadNextPageCommand).CanExecuteChanged += ProbeCanExecute;
            ((ICommand)sut.LoadPreviousPageCommand).CanExecuteChanged += ProbeCanExecute;
            ((ICommand)sut.RefreshPageCommand).CanExecuteChanged += ProbeCanExecute;

            var first = sut.ApplyProjectContextAsync(new EmailListProjectContext(1, "1", "A", "1 — A"));
            await WaitUntilAsync(() => gateway.Calls >= 1);
            var second = sut.RefreshPageAsync();
            Assert.False(second.IsCompleted);

            var released = 0;
            while (released < 8 && (!first.IsCompleted || !second.IsCompleted))
            {
                gateway.ReleaseOne();
                released++;
                await Task.Delay(15).ConfigureAwait(true);
            }

            await first.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            await second.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            Assert.Contains(false, gateway.CallOnDispatcher);
            Assert.Empty(offDispatcher);
            Assert.Equal(1, sut.CurrentPageNumber);
            Assert.True(sut.HasNextPage);
            Assert.True(sut.DisplayedCount > 0);
            Assert.False(string.IsNullOrWhiteSpace(sut.PageInfo));
            Assert.NotNull(sut.ActiveProjectGroup);
            Assert.False(sut.ActiveProjectGroup!.IsLoading);
            Assert.False(sut.IsBusy);
            Assert.False(gate.IsBusy);
        });

    private static EmailListViewModel CreateList(
        IEmailGateway? gateway = null,
        ICurrentProjectContext? project = null,
        MailboxReloadOrchestrator? reload = null) =>
        new(
            gateway ?? new EmailListViewModelTestFixtures.PagingEmailGateway(),
            threadLinkQuery: null,
            new EmailListViewModelTestFixtures.StubAuthService(),
            currentProject: project,
            reloadOrchestrator: reload);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 8000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(true);
        }
    }

    private sealed class LabelListGateway : EmailListViewModelTestFixtures.PagingEmailGateway
    {
        public override Task<IReadOnlyList<GmailLabelInfo>> GetMailboxLabelsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GmailLabelInfo>>(
            [
                new GmailLabelInfo("L1", "Work"),
                new GmailLabelInfo("L2", "Clients"),
            ]);
    }

    private sealed class ThrowingPagingGateway : EmailListViewModelTestFixtures.PagingEmailGateway
    {
        public override Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Gmail exploded");
    }

    private sealed class BarrierPagingGateway : EmailListViewModelTestFixtures.PagingEmailGateway
    {
        private readonly SemaphoreSlim _release = new(0, 10);
        private readonly List<bool> _callOnDispatcher = [];
        private int _calls;

        public int Calls => _calls;

        public IReadOnlyList<bool> CallOnDispatcher
        {
            get
            {
                lock (_callOnDispatcher)
                {
                    return [.. _callOnDispatcher];
                }
            }
        }

        public void ReleaseOne() => _release.Release();

        public override async Task<EmailMailboxPage> GetMailboxPageAsync(
            EmailMailboxQuery query,
            string? pageToken = null,
            CancellationToken cancellationToken = default)
        {
            var onDispatcher = UiThread.Dispatcher?.CheckAccess() == true;
            lock (_callOnDispatcher)
            {
                _callOnDispatcher.Add(onDispatcher);
            }

            Interlocked.Increment(ref _calls);
            await _release.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await base.GetMailboxPageAsync(query, pageToken, cancellationToken).ConfigureAwait(false);
        }
    }
}
