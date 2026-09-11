namespace SiNet.Application.Billing;

public sealed class MemoryBillingPreparationStore : IBillingPreparationStore
{
    private readonly Dictionary<int, BillingPreparationRequestRecord> _rows = [];
    private int _nextId = 1;

    public Task<BillingPreparationRequestRecord?> GetActiveByMasterPlanProjectIdAsync(
        int masterPlanProjectId,
        CancellationToken cancellationToken = default)
    {
        var match = _rows.Values.FirstOrDefault(r =>
            r.MasterPlanProjectId == masterPlanProjectId && r.Status.IsActiveCycle());
        return Task.FromResult(match);
    }

    public Task<BillingPreparationRequestRecord?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        _rows.TryGetValue(id, out var row);
        return Task.FromResult(row);
    }

    public Task<BillingPreparationRequestRecord?> GetByTaskIdAsync(
        int taskId,
        CancellationToken cancellationToken = default)
    {
        var match = _rows.Values.FirstOrDefault(r => r.TaskId == taskId);
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<BillingPreparationRequestRecord>> ListActiveAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BillingPreparationRequestRecord>>(
            _rows.Values.Where(r => r.Status.IsActiveCycle()).ToList());

    public Task<IReadOnlyList<BillingPreparationRequestRecord>> ListAwaitingConfirmationAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BillingPreparationRequestRecord>>(
            _rows.Values.Where(r => r.Status == BillingPreparationStatus.AwaitingMasterPlanConfirmation).ToList());

    public Task<BillingPreparationRequestRecord> InsertAsync(
        BillingPreparationRequestRecord request,
        CancellationToken cancellationToken = default)
    {
        var id = request.Id > 0 ? request.Id : _nextId++;
        if (id >= _nextId)
            _nextId = id + 1;
        var saved = request with { Id = id };
        _rows[id] = saved;
        return Task.FromResult(saved);
    }

    public Task<BillingPreparationRequestRecord> UpdateAsync(
        BillingPreparationRequestRecord request,
        CancellationToken cancellationToken = default)
    {
        if (request.Id <= 0 || !_rows.ContainsKey(request.Id))
            throw new InvalidOperationException("Cannot update unsaved preparation request.");
        _rows[request.Id] = request;
        return Task.FromResult(request);
    }

    public Task<IReadOnlyList<int>> FindHourReportIdsInOtherRequestsAsync(
        IReadOnlyList<int> hourReportIds,
        int? excludeRequestId,
        CancellationToken cancellationToken = default)
    {
        var set = hourReportIds.ToHashSet();
        var hits = _rows.Values
            .Where(r => excludeRequestId is null || r.Id != excludeRequestId.Value)
            .Where(r => r.Status.IsActiveCycle() || r.Status == BillingPreparationStatus.Completed)
            .SelectMany(r => r.Hours.SelectMany(h => h.Reports.Select(x => x.HoursReportId)))
            .Where(set.Contains)
            .Distinct()
            .ToList();
        return Task.FromResult<IReadOnlyList<int>>(hits);
    }
}

public sealed class FixedBillingPreparationActor(BillingActor actor) : IBillingPreparationActor
{
    public Task<BillingActor> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(actor);
}

public sealed class MemoryBillingPreparationTaskPort : IBillingPreparationTaskPort
{
    public int LastTaskId { get; private set; }
    public int CreateCalls { get; set; }
    public bool OpenTaskExists { get; set; }
    public string? LastBody { get; private set; }

    public bool ThrowOnCreate { get; set; }

    public Task<int> CreatePrepareBillTaskAsync(
        int siNetProjectId,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnCreate)
            throw new InvalidOperationException("יצירת משימת הכנת חשבון נכשלה.");

        CreateCalls++;
        LastBody = body;
        LastTaskId = 9000 + CreateCalls;
        OpenTaskExists = true;
        return Task.FromResult(LastTaskId);
    }

    public Task<bool> HasOpenPrepareBillTaskAsync(
        int siNetProjectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OpenTaskExists && CreateCalls > 0);
}

public sealed class MemoryBillingPreparationProjectMapper(int? siNetProjectId = 12)
    : IBillingPreparationProjectMapper
{
    public Task<int?> TryResolveSiNetProjectIdAsync(
        string? projectNumber,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(siNetProjectId);
}

public sealed class MemoryBillingPreparationComponentSource : IBillingPreparationComponentSource
{
    public BillingPreparationSnapshotLoad Load { get; set; } = new(
        SnapshotAvailable: false,
        LatestBackupUtc: null,
        CustomerName: null,
        ProjectNumber: null,
        ProjectName: null,
        Stages: [],
        HourlySubContracts: [],
        HourReports: []);

    public Task<BillingPreparationSnapshotLoad> LoadAsync(
        int masterPlanProjectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Load);
}
