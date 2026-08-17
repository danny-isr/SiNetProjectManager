using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class GmailMailboxChangeDetectorTests
{
    [Fact]
    public async Task CheckForChanges_no_messagesAdded_advances_immediately()
    {
        var api = new FakeHistoryApi
        {
            Pages =
            [
                new GmailHistoryListPage(200, HasMessagesAdded: false, NextPageToken: null),
            ],
        };
        var detector = new GmailMailboxChangeDetector(api, NullLogger.Instance);
        detector.CommitBaseline(100);

        var outcome = await detector.CheckForChangesAsync(EmailMailboxScope.Inbox);

        Assert.Equal(GmailHistoryCheckOutcome.NoRelevantChanges, outcome);
        Assert.Equal(200UL, detector.LastHistoryId);
        Assert.Null(detector.PendingHistoryId);
    }

    [Fact]
    public async Task CheckForChanges_messagesAdded_stores_pending_without_advancing()
    {
        var api = new FakeHistoryApi
        {
            Pages =
            [
                new GmailHistoryListPage(150, HasMessagesAdded: false, NextPageToken: "p2"),
                new GmailHistoryListPage(250, HasMessagesAdded: true, NextPageToken: null),
            ],
        };
        var detector = new GmailMailboxChangeDetector(api, NullLogger.Instance);
        detector.CommitBaseline(100);

        var outcome = await detector.CheckForChangesAsync(EmailMailboxScope.Inbox);

        Assert.Equal(GmailHistoryCheckOutcome.ReloadRequired, outcome);
        Assert.Equal(100UL, detector.LastHistoryId);
        Assert.Equal(250UL, detector.PendingHistoryId);
        Assert.Equal(2, api.PageCalls);
    }

    [Fact]
    public async Task CheckForChanges_pages_all_tokens_before_decision()
    {
        var api = new FakeHistoryApi
        {
            Pages =
            [
                new GmailHistoryListPage(110, HasMessagesAdded: false, NextPageToken: "a"),
                new GmailHistoryListPage(120, HasMessagesAdded: false, NextPageToken: "b"),
                new GmailHistoryListPage(130, HasMessagesAdded: false, NextPageToken: null),
            ],
        };
        var detector = new GmailMailboxChangeDetector(api, NullLogger.Instance);
        detector.CommitBaseline(100);

        await detector.CheckForChangesAsync(EmailMailboxScope.AllMail);

        Assert.Equal(3, api.PageCalls);
        Assert.Null(api.LastLabelId);
        Assert.Equal(130UL, detector.LastHistoryId);
    }

    [Fact]
    public async Task CheckForChanges_inbox_passes_labelId_INBOX()
    {
        var api = new FakeHistoryApi
        {
            Pages = [new GmailHistoryListPage(101, false, null)],
        };
        var detector = new GmailMailboxChangeDetector(api, NullLogger.Instance);
        detector.CommitBaseline(100);

        await detector.CheckForChangesAsync(EmailMailboxScope.Inbox);

        Assert.Equal("INBOX", api.LastLabelId);
    }

    [Fact]
    public async Task CheckForChanges_expired_returns_HistoryExpired_without_changing_checkpoint()
    {
        var api = new FakeHistoryApi { ThrowExpired = true };
        var detector = new GmailMailboxChangeDetector(api, NullLogger.Instance);
        detector.CommitBaseline(100);

        var outcome = await detector.CheckForChangesAsync(EmailMailboxScope.Inbox);

        Assert.Equal(GmailHistoryCheckOutcome.HistoryExpired, outcome);
        Assert.Equal(100UL, detector.LastHistoryId);
    }

    [Fact]
    public async Task CheckForChanges_transient_leaves_checkpoint_unchanged()
    {
        var api = new FakeHistoryApi { ThrowTransient = true };
        var detector = new GmailMailboxChangeDetector(api, NullLogger.Instance);
        detector.CommitBaseline(100);

        var outcome = await detector.CheckForChangesAsync(EmailMailboxScope.Inbox);

        Assert.Equal(GmailHistoryCheckOutcome.TransientFailure, outcome);
        Assert.Equal(100UL, detector.LastHistoryId);
        Assert.Null(detector.PendingHistoryId);
    }

    [Fact]
    public void CommitPendingCheckpoint_after_reload_advances_last()
    {
        var detector = new GmailMailboxChangeDetector(new FakeHistoryApi(), NullLogger.Instance);
        detector.CommitBaseline(100);
        detector.CommitPendingCheckpoint();
        Assert.Equal(100UL, detector.LastHistoryId);
    }

    private sealed class FakeHistoryApi : IGmailHistoryApi
    {
        public List<GmailHistoryListPage> Pages { get; init; } = [];
        public int PageCalls { get; private set; }
        public string? LastLabelId { get; private set; }
        public bool ThrowExpired { get; init; }
        public bool ThrowTransient { get; init; }

        public Task<ulong?> GetProfileHistoryIdAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ulong?>(999);

        public Task<GmailHistoryListPage> ListHistoryPageAsync(
            ulong startHistoryId,
            string? labelId,
            IReadOnlyList<string> historyTypes,
            string? pageToken,
            CancellationToken cancellationToken = default)
        {
            LastLabelId = labelId;
            if (ThrowExpired)
                throw new GmailHistoryExpiredException("expired");
            if (ThrowTransient)
                throw new InvalidOperationException("network");

            var index = PageCalls;
            PageCalls++;
            if (index >= Pages.Count)
                return Task.FromResult(new GmailHistoryListPage(startHistoryId, false, null));
            return Task.FromResult(Pages[index]);
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public static NullLogger Instance { get; } = new();
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
