namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Shared single-flight mailbox reload gate with one-slot <see cref="ReloadPending"/> coalesce.
/// All full loads (manual, filters, History) must go through this orchestrator.
/// </summary>
public sealed class MailboxReloadOrchestrator
{
    private int _gate;
    private int _reloadPending;
    private readonly object _coalesceLock = new();
    private Func<CancellationToken, Task>? _coalescedReload;
    private Action? _coalescedSuccess;

    /// <summary>True while a reload is executing.</summary>
    public bool IsBusy => Volatile.Read(ref _gate) != 0;

    /// <summary>True when a follow-up reload was requested while busy.</summary>
    public bool ReloadPending => Volatile.Read(ref _reloadPending) != 0;

    /// <summary>
    /// Runs <paramref name="reloadAsync"/> now, or sets <see cref="ReloadPending"/> if busy.
    /// After the active run completes successfully, executes at most one coalesced follow-up
    /// using the <b>latest</b> reload/success callbacks registered while busy.
    /// </summary>
    public async Task RequestAsync(
        Func<CancellationToken, Task> reloadAsync,
        Action? onSuccessfulReload = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reloadAsync);

        if (Interlocked.CompareExchange(ref _gate, 1, 0) != 0)
        {
            lock (_coalesceLock)
            {
                _coalescedReload = reloadAsync;
                if (onSuccessfulReload is not null)
                    _coalescedSuccess = onSuccessfulReload;
            }

            Interlocked.Exchange(ref _reloadPending, 1);
            return;
        }

        try
        {
            var currentReload = reloadAsync;
            var currentSuccess = onSuccessfulReload;

            do
            {
                Interlocked.Exchange(ref _reloadPending, 0);
                await currentReload(cancellationToken).ConfigureAwait(false);
                currentSuccess?.Invoke();

                lock (_coalesceLock)
                {
                    if (_coalescedReload is not null)
                        currentReload = _coalescedReload;
                    if (_coalescedSuccess is not null)
                        currentSuccess = _coalescedSuccess;
                    _coalescedReload = null;
                    _coalescedSuccess = null;
                }
            }
            while (Interlocked.CompareExchange(ref _reloadPending, 0, 1) == 1);
        }
        finally
        {
            Interlocked.Exchange(ref _gate, 0);

            // Lost-wakeup between loop exit and Leave.
            if (Volatile.Read(ref _reloadPending) != 0
                && Interlocked.CompareExchange(ref _gate, 1, 0) == 0)
            {
                try
                {
                    Func<CancellationToken, Task> currentReload;
                    Action? currentSuccess;
                    lock (_coalesceLock)
                    {
                        currentReload = _coalescedReload ?? reloadAsync;
                        currentSuccess = _coalescedSuccess ?? onSuccessfulReload;
                        _coalescedReload = null;
                        _coalescedSuccess = null;
                    }

                    do
                    {
                        Interlocked.Exchange(ref _reloadPending, 0);
                        await currentReload(cancellationToken).ConfigureAwait(false);
                        currentSuccess?.Invoke();

                        lock (_coalesceLock)
                        {
                            if (_coalescedReload is not null)
                                currentReload = _coalescedReload;
                            if (_coalescedSuccess is not null)
                                currentSuccess = _coalescedSuccess;
                            _coalescedReload = null;
                            _coalescedSuccess = null;
                        }
                    }
                    while (Interlocked.CompareExchange(ref _reloadPending, 0, 1) == 1);
                }
                finally
                {
                    Interlocked.Exchange(ref _gate, 0);
                }
            }
        }
    }
}
