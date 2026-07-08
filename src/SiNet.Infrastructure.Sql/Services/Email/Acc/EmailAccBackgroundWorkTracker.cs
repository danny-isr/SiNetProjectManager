using SiNet.Application.Email.Acc;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

internal sealed class EmailAccBackgroundWorkTracker : IEmailAccBackgroundWorkTracker
{
    private int _activeCount;

    public int ActiveCount => Volatile.Read(ref _activeCount);

    public event Action<int>? ActiveCountChanged;

    public IDisposable BeginWork()
    {
        var count = Interlocked.Increment(ref _activeCount);
        ActiveCountChanged?.Invoke(count);
        return new Scope(this);
    }

    private void EndWork()
    {
        var count = Interlocked.Decrement(ref _activeCount);
        if (count < 0)
        {
            Interlocked.Exchange(ref _activeCount, 0);
            count = 0;
        }

        ActiveCountChanged?.Invoke(count);
    }

    private sealed class Scope(EmailAccBackgroundWorkTracker owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            owner.EndWork();
        }
    }
}
