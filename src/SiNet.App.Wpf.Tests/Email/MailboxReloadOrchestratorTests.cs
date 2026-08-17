using SiNet.Application.Abstractions.Email;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class MailboxReloadOrchestratorTests
{
    [Fact]
    public async Task RequestAsync_when_busy_sets_pending_then_runs_one_follow_up_and_commits()
    {
        var gate = new MailboxReloadOrchestrator();
        var reloadStarts = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReloadMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloadCount = 0;
        var commitCount = 0;
        ulong? pending = null;
        ulong? committed = null;

        // Active UI reload — must not commit History checkpoint.
        var first = gate.RequestAsync(
            async _ =>
            {
                Interlocked.Increment(ref reloadCount);
                reloadStarts.TrySetResult();
                await firstReloadMayFinish.Task.ConfigureAwait(false);
            },
            onSuccessfulReload: null);

        await reloadStarts.Task.ConfigureAwait(false);
        Assert.True(gate.IsBusy);

        // History messagesAdded while busy.
        pending = 42;
        var second = gate.RequestAsync(
            async _ =>
            {
                Interlocked.Increment(ref reloadCount);
                await Task.CompletedTask.ConfigureAwait(false);
            },
            onSuccessfulReload: () =>
            {
                Interlocked.Increment(ref commitCount);
                committed = pending;
                pending = null;
            });

        Assert.True(gate.ReloadPending);
        Assert.Equal(42UL, pending);

        firstReloadMayFinish.TrySetResult();
        await Task.WhenAll(first, second).ConfigureAwait(false);

        Assert.Equal(2, reloadCount);
        Assert.Equal(1, commitCount);
        Assert.Equal(42UL, committed);
        Assert.Null(pending);
        Assert.False(gate.IsBusy);
        Assert.False(gate.ReloadPending);
    }

    [Fact]
    public async Task RequestAsync_when_free_runs_once_and_commits()
    {
        var gate = new MailboxReloadOrchestrator();
        var reloads = 0;
        var commits = 0;

        await gate.RequestAsync(
            _ =>
            {
                Interlocked.Increment(ref reloads);
                return Task.CompletedTask;
            },
            onSuccessfulReload: () => Interlocked.Increment(ref commits));

        Assert.Equal(1, reloads);
        Assert.Equal(1, commits);
    }
}
