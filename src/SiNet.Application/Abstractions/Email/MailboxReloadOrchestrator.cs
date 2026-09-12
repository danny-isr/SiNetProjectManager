namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Shared single-flight mailbox reload gate with one-slot <see cref="ReloadPending"/> coalesce.
/// All full loads (manual, filters, History) must go through this orchestrator.
/// </summary>
/// <remarks>
/// The orchestrator is context-neutral: <c>ConfigureAwait(false)</c> may resume on the thread pool.
/// Callbacks that mutate WPF-bound collections must marshal onto the dispatcher themselves.
/// <para>
/// Single-flight: the first caller always runs. A caller that arrives while busy never completes
/// until the follow-up pass that includes the latest coalesced request has finished (or faulted).
/// At most one follow-up runs per busy period (latest reload/success callbacks win).
/// </para>
/// </remarks>
public sealed class MailboxReloadOrchestrator
{
    private static readonly AsyncLocal<bool> OwnerPass = new();

    private readonly object _lock = new();
    private int _reloadPending;
    private bool _loopRunning;
    private Func<CancellationToken, Task>? _coalescedReload;
    private Action? _coalescedSuccess;
    private CancellationToken _coalescedToken;
    private TaskCompletionSource? _coalesceWaiters;

    /// <summary>True while a reload pass is executing or a follow-up is queued to run.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_lock)
            {
                return _loopRunning;
            }
        }
    }

    /// <summary>True when a follow-up reload was requested while busy.</summary>
    public bool ReloadPending => Volatile.Read(ref _reloadPending) != 0;

    /// <summary>
    /// Runs <paramref name="reloadAsync"/> now, or coalesces it as the latest follow-up if busy.
    /// After the active run completes, executes at most one coalesced follow-up using the
    /// <b>latest</b> reload/success callbacks registered while busy.
    /// </summary>
    public Task RequestAsync(
        Func<CancellationToken, Task> reloadAsync,
        Action? onSuccessfulReload = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reloadAsync);

        lock (_lock)
        {
            if (_loopRunning)
            {
                _coalescedReload = reloadAsync;
                if (onSuccessfulReload is not null)
                {
                    _coalescedSuccess = onSuccessfulReload;
                }

                _coalescedToken = cancellationToken;
                Volatile.Write(ref _reloadPending, 1);
                _coalesceWaiters ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                // Nested RequestAsync from inside an in-flight pass must not wait for the
                // follow-up TCS — that would deadlock the owner. External callers still wait.
                if (OwnerPass.Value)
                {
                    return Task.CompletedTask;
                }

                return _coalesceWaiters.Task;
            }

            _loopRunning = true;
        }

        return RunOwnerAsync(reloadAsync, onSuccessfulReload, cancellationToken);
    }

    private async Task RunOwnerAsync(
        Func<CancellationToken, Task> reloadAsync,
        Action? onSuccessfulReload,
        CancellationToken cancellationToken)
    {
        var currentReload = reloadAsync;
        var currentSuccess = onSuccessfulReload;
        var currentToken = cancellationToken;
        TaskCompletionSource? passWaiters = null;
        Exception? ownerError = null;
        OwnerPass.Value = true;

        try
        {
            while (true)
            {
                try
                {
                    await currentReload(currentToken).ConfigureAwait(false);
                    currentSuccess?.Invoke();
                    passWaiters?.TrySetResult();
                }
                catch (Exception ex)
                {
                    passWaiters?.TrySetException(ex);
                    ownerError ??= ex;
                }

                lock (_lock)
                {
                    if (Volatile.Read(ref _reloadPending) == 0)
                    {
                        _loopRunning = false;
                        if (_coalesceWaiters is not null)
                        {
                            if (ownerError is not null)
                            {
                                _coalesceWaiters.TrySetException(ownerError);
                            }
                            else
                            {
                                _coalesceWaiters.TrySetResult();
                            }

                            _coalesceWaiters = null;
                        }

                        break;
                    }

                    Volatile.Write(ref _reloadPending, 0);
                    currentReload = _coalescedReload ?? currentReload;
                    currentSuccess = _coalescedSuccess ?? currentSuccess;
                    currentToken = _coalescedToken;
                    _coalescedReload = null;
                    _coalescedSuccess = null;
                    passWaiters = _coalesceWaiters;
                    _coalesceWaiters = null;
                }
            }

            if (ownerError is not null)
            {
                throw ownerError;
            }
        }
        catch (Exception ex) when (!ReferenceEquals(ex, ownerError))
        {
            lock (_lock)
            {
                _loopRunning = false;
                _coalesceWaiters?.TrySetException(ex);
                _coalesceWaiters = null;
            }

            throw;
        }
        finally
        {
            OwnerPass.Value = false;
        }
    }
}
