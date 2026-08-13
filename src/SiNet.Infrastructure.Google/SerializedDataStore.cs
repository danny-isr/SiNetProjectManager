using Google.Apis.Util.Store;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Serializes <see cref="IDataStore"/> access so parallel System Status Google contributors cannot
/// hit <c>IOException</c> "file is being used by another process" on the token store.
/// </summary>
internal sealed class SerializedDataStore(IDataStore inner) : IDataStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly IDataStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task StoreAsync<T>(string key, T value)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _inner.StoreAsync(key, value).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task DeleteAsync<T>(string key)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _inner.DeleteAsync<T>(key).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<T> GetAsync<T>(string key)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await _inner.GetAsync<T>(key).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _inner.ClearAsync().ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
