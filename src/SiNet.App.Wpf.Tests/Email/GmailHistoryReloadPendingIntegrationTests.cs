using SiNet.Application.Abstractions.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

/// <summary>
/// Critical race: messagesAdded while reload busy must not consume the checkpoint
/// until a coalesced follow-up reload succeeds.
/// </summary>
public sealed class GmailHistoryReloadPendingIntegrationTests
{
    [Fact]
    public async Task MessagesAdded_while_reload_busy_keeps_pending_until_follow_up_commits()
    {
        var gate = new MailboxReloadOrchestrator();
        var api = new ScriptedHistoryApi();
        var detector = new SiNet.Infrastructure.Google.GmailMailboxChangeDetector(
            api,
            new NullLogger());
        detector.CommitBaseline(100);

        var reloadStarts = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloadCount = 0;

        // Active UI reload (manual / filter).
        var activeReload = gate.RequestAsync(
            async _ =>
            {
                Interlocked.Increment(ref reloadCount);
                reloadStarts.TrySetResult();
                await firstMayFinish.Task.ConfigureAwait(false);
            },
            onSuccessfulReload: () =>
            {
                // Ordinary UI reload must NOT commit history checkpoint from query results.
            });

        await reloadStarts.Task.ConfigureAwait(false);

        api.Pages =
        [
            new GmailHistoryListPage(300, HasMessagesAdded: true, NextPageToken: null),
        ];

        var outcome = await detector.CheckForChangesAsync(EmailMailboxScope.Inbox);
        Assert.Equal(GmailHistoryCheckOutcome.ReloadRequired, outcome);
        Assert.Equal(100UL, detector.LastHistoryId);
        Assert.Equal(300UL, detector.PendingHistoryId);

        var historyReload = gate.RequestAsync(
            async _ =>
            {
                Interlocked.Increment(ref reloadCount);
                await Task.CompletedTask.ConfigureAwait(false);
            },
            onSuccessfulReload: detector.CommitPendingCheckpoint);

        Assert.True(gate.ReloadPending);
        Assert.Equal(300UL, detector.PendingHistoryId);
        Assert.Equal(100UL, detector.LastHistoryId);

        firstMayFinish.TrySetResult();
        await Task.WhenAll(activeReload, historyReload).ConfigureAwait(false);

        Assert.Equal(2, reloadCount);
        Assert.Equal(300UL, detector.LastHistoryId);
        Assert.Null(detector.PendingHistoryId);
        Assert.False(gate.ReloadPending);
    }

    private sealed class ScriptedHistoryApi : IGmailHistoryApi
    {
        public List<GmailHistoryListPage> Pages { get; set; } = [];

        public Task<ulong?> GetProfileHistoryIdAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ulong?>(100);

        public Task<GmailHistoryListPage> ListHistoryPageAsync(
            ulong startHistoryId,
            string? labelId,
            IReadOnlyList<string> historyTypes,
            string? pageToken,
            CancellationToken cancellationToken = default)
        {
            if (Pages.Count == 0)
                return Task.FromResult(new GmailHistoryListPage(startHistoryId, false, null));
            var page = Pages[0];
            Pages.RemoveAt(0);
            return Task.FromResult(page);
        }
    }

    private sealed class NullLogger : SiNet.Application.Abstractions.Logging.IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
