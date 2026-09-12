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
        Assert.False(second.IsCompleted);

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

    [Fact]
    public async Task RequestAsync_nested_from_inside_pass_does_not_deadlock()
    {
        var gate = new MailboxReloadOrchestrator();
        var nestedCompleted = false;

        await gate.RequestAsync(async _ =>
        {
            await gate.RequestAsync(_ =>
            {
                nestedCompleted = true;
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }).ConfigureAwait(false);

        Assert.True(nestedCompleted);
        Assert.False(gate.IsBusy);
        Assert.False(gate.ReloadPending);
    }

    [Fact]
    public async Task RequestAsync_external_waiter_does_not_complete_until_follow_up_runs()
    {
        var gate = new MailboxReloadOrchestrator();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastPass = 0;

        var first = gate.RequestAsync(async _ =>
        {
            lastPass = 1;
            firstEntered.TrySetResult();
            await firstMayFinish.Task.ConfigureAwait(false);
        });

        await firstEntered.Task.ConfigureAwait(false);
        var second = gate.RequestAsync(async _ =>
        {
            lastPass = 2;
            secondEntered.TrySetResult();
            await Task.CompletedTask.ConfigureAwait(false);
        });

        Assert.False(second.IsCompleted);
        Assert.False(secondEntered.Task.IsCompleted);

        firstMayFinish.TrySetResult();
        await Task.WhenAll(first, second).ConfigureAwait(false);

        Assert.True(secondEntered.Task.IsCompleted);
        Assert.Equal(2, lastPass);
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public async Task RequestAsync_latest_null_success_callback_replaces_older_callback()
    {
        var gate = new MailboxReloadOrchestrator();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastPass = 0;
        var bSuccess = 0;
        var cSuccess = 0;

        var first = gate.RequestAsync(async _ =>
        {
            lastPass = 1;
            firstEntered.TrySetResult();
            await firstMayFinish.Task.ConfigureAwait(false);
        });

        await firstEntered.Task.ConfigureAwait(false);

        var second = gate.RequestAsync(
            _ =>
            {
                lastPass = 2;
                return Task.CompletedTask;
            },
            onSuccessfulReload: () => Interlocked.Increment(ref bSuccess));

        var third = gate.RequestAsync(
            _ =>
            {
                lastPass = 3;
                return Task.CompletedTask;
            },
            onSuccessfulReload: null);

        Assert.False(second.IsCompleted);
        Assert.False(third.IsCompleted);

        firstMayFinish.TrySetResult();
        await Task.WhenAll(first, second, third).ConfigureAwait(false);

        Assert.Equal(3, lastPass);
        Assert.Equal(0, bSuccess);
        Assert.Equal(0, cSuccess);
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public async Task RequestAsync_latest_success_callback_replaces_older_null()
    {
        var gate = new MailboxReloadOrchestrator();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastPass = 0;
        var cSuccess = 0;

        var first = gate.RequestAsync(async _ =>
        {
            lastPass = 1;
            firstEntered.TrySetResult();
            await firstMayFinish.Task.ConfigureAwait(false);
        });

        await firstEntered.Task.ConfigureAwait(false);

        var second = gate.RequestAsync(
            _ =>
            {
                lastPass = 2;
                return Task.CompletedTask;
            },
            onSuccessfulReload: null);

        var third = gate.RequestAsync(
            _ =>
            {
                lastPass = 3;
                return Task.CompletedTask;
            },
            onSuccessfulReload: () => Interlocked.Increment(ref cSuccess));

        firstMayFinish.TrySetResult();
        await Task.WhenAll(first, second, third).ConfigureAwait(false);

        Assert.Equal(3, lastPass);
        Assert.Equal(1, cSuccess);
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public async Task RequestAsync_owner_pass_does_not_leak_to_another_instance()
    {
        var gateA = new MailboxReloadOrchestrator();
        var gateB = new MailboxReloadOrchestrator();
        var bFirstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bFirstMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bFollowUpEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await gateA.RequestAsync(async _ =>
        {
            var bFirst = gateB.RequestAsync(async _ =>
            {
                bFirstEntered.TrySetResult();
                await bFirstMayFinish.Task.ConfigureAwait(false);
            });

            await bFirstEntered.Task.ConfigureAwait(false);

            var bSecond = gateB.RequestAsync(_ =>
            {
                bFollowUpEntered.TrySetResult();
                return Task.CompletedTask;
            });

            Assert.False(bSecond.IsCompleted);
            Assert.False(bFollowUpEntered.Task.IsCompleted);

            bFirstMayFinish.TrySetResult();
            await Task.WhenAll(bFirst, bSecond).ConfigureAwait(false);
            Assert.True(bFollowUpEntered.Task.IsCompleted);
        }).ConfigureAwait(false);

        Assert.False(gateA.IsBusy);
        Assert.False(gateB.IsBusy);
    }
}
