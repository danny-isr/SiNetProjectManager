using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Google;

internal sealed record GmailLabelRecord(
    string Id,
    string Name,
    string? BackgroundColor = null,
    string? TextColor = null,
    string? Type = null,
    int? MessagesUnread = null);

internal interface IGmailLabelDirectory
{
    string SessionKey { get; }

    Task<IReadOnlyList<GmailLabelRecord>> ListAsync(CancellationToken cancellationToken);
}

internal interface IGmailLabelCatalog
{
    int Generation { get; }

    int ListCallCount { get; }

    string? CachedSessionKey { get; }

    Task<IReadOnlyDictionary<string, GmailLabelRecord>> GetMapAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, GmailLabelRecord>> ResolveForMessageAsync(
        string? messageId,
        IEnumerable<string?>? labelIds,
        CancellationToken cancellationToken = default);

    void NotifyCreated(string id, string name);

    void NotifyRenamed(string id, string newName);

    void NotifyDeleted(string id);

    void Invalidate(string reason);
}

internal sealed class GmailLabelCatalog : IGmailLabelCatalog
{
    private readonly IGmailLabelDirectory _directory;
    private readonly IAppLogger _logger;
    private readonly object _mapLock = new();

    private IReadOnlyDictionary<string, GmailLabelRecord>? _map;
    private bool _hasDirectorySnapshot;
    private string? _sessionKey;
    private int _generation;
    private int _listCallCount;
    private Task<IReadOnlyDictionary<string, GmailLabelRecord>>? _refreshInFlight;

    public GmailLabelCatalog(IGmailLabelDirectory directory, IAppLogger logger)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Generation => _generation;

    public int ListCallCount => _listCallCount;

    public string? CachedSessionKey => _sessionKey;

    public async Task<IReadOnlyDictionary<string, GmailLabelRecord>> GetMapAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_mapLock)
        {
            DropCacheIfSessionChangedLocked();
            if (_map is not null && _hasDirectorySnapshot)
                return _map;
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, GmailLabelRecord>> ResolveForMessageAsync(
        string? messageId,
        IEnumerable<string?>? labelIds,
        CancellationToken cancellationToken = default)
    {
        var map = await GetMapAsync(cancellationToken).ConfigureAwait(false);
        var unknown = CollectUnknown(map, labelIds);
        if (unknown.Count == 0)
            return map;

        var generationBefore = _generation;
        foreach (var unknownId in unknown)
        {
            _logger.Info(
                $"[GmailLabels] messageId={messageId ?? "(none)"} unknownLabelId={unknownId} cacheGeneration={generationBefore} action=refresh-label-map");
        }

        var refreshed = await RefreshAsync(cancellationToken).ConfigureAwait(false);
        foreach (var unknownId in unknown)
        {
            var resolved = refreshed.TryGetValue(unknownId, out var label);
            _logger.Info(
                $"[GmailLabels] messageId={messageId ?? "(none)"} unknownLabelId={unknownId} resolved={(resolved ? "true" : "false")} labelName={(resolved ? label!.Name : "(none)")}");
            if (!resolved)
            {
                _logger.Warn(
                    $"[GmailLabels] messageId={messageId ?? "(none)"} unknownLabelId={unknownId} still unresolved after Labels.List");
            }
        }

        return refreshed;
    }

    public void NotifyCreated(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            return;

        lock (_mapLock)
        {
            DropCacheIfSessionChangedLocked();
            var next = _map is null
                ? new Dictionary<string, GmailLabelRecord>(StringComparer.Ordinal)
                : new Dictionary<string, GmailLabelRecord>(_map, StringComparer.Ordinal);
            next[id] = next.TryGetValue(id, out var existing)
                ? existing with { Name = name }
                : new GmailLabelRecord(id, name);
            _map = next;
            _generation++;
        }
    }

    public void NotifyRenamed(string id, string newName)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(newName))
            return;

        lock (_mapLock)
        {
            DropCacheIfSessionChangedLocked();
            if (_map is null || !_map.TryGetValue(id, out var existing))
            {
                InvalidateLocked("rename-miss");
                return;
            }

            var next = new Dictionary<string, GmailLabelRecord>(_map, StringComparer.Ordinal)
            {
                [id] = existing with { Name = newName.Trim() }
            };
            _map = next;
            _generation++;
        }
    }

    public void NotifyDeleted(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        lock (_mapLock)
        {
            DropCacheIfSessionChangedLocked();
            if (_map is null)
                return;

            var next = new Dictionary<string, GmailLabelRecord>(_map, StringComparer.Ordinal);
            next.Remove(id);
            _map = next;
            _generation++;
        }
    }

    public void Invalidate(string reason)
    {
        lock (_mapLock)
        {
            InvalidateLocked(reason);
        }
    }

    private async Task<IReadOnlyDictionary<string, GmailLabelRecord>> RefreshAsync(
        CancellationToken cancellationToken)
    {
        Task<IReadOnlyDictionary<string, GmailLabelRecord>> refresh;
        lock (_mapLock)
        {
            DropCacheIfSessionChangedLocked();
            if (_refreshInFlight is not null)
            {
                refresh = _refreshInFlight;
            }
            else
            {
                refresh = LoadFromDirectoryAsync();
                _refreshInFlight = refresh;
            }
        }

        try
        {
            return await refresh.ConfigureAwait(false);
        }
        finally
        {
            lock (_mapLock)
            {
                if (ReferenceEquals(_refreshInFlight, refresh))
                    _refreshInFlight = null;
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, GmailLabelRecord>> LoadFromDirectoryAsync()
    {
        var listed = await _directory.ListAsync(CancellationToken.None).ConfigureAwait(false);
        var map = listed
            .Where(static label => !string.IsNullOrWhiteSpace(label.Id))
            .GroupBy(static label => label.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        lock (_mapLock)
        {
            _sessionKey = _directory.SessionKey;
            if (_map is not null)
            {
                foreach (var (id, record) in _map)
                    map.TryAdd(id, record);
            }

            _map = map;
            _hasDirectorySnapshot = true;
            _generation++;
            _listCallCount++;
            return map;
        }
    }

    private void DropCacheIfSessionChangedLocked()
    {
        var current = _directory.SessionKey;
        if (string.Equals(_sessionKey, current, StringComparison.Ordinal))
            return;

        _map = null;
        _hasDirectorySnapshot = false;
        _sessionKey = current;
        _generation++;
        _refreshInFlight = null;
    }

    private void InvalidateLocked(string reason)
    {
        _ = reason;
        _map = null;
        _hasDirectorySnapshot = false;
        _refreshInFlight = null;
        _generation++;
    }

    private static List<string> CollectUnknown(
        IReadOnlyDictionary<string, GmailLabelRecord> map,
        IEnumerable<string?>? labelIds)
    {
        if (labelIds is null)
            return [];

        var unknown = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var labelId in labelIds)
        {
            if (string.IsNullOrWhiteSpace(labelId) || !seen.Add(labelId))
                continue;
            if (!map.ContainsKey(labelId))
                unknown.Add(labelId);
        }

        return unknown;
    }
}
