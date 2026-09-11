using Microsoft.EntityFrameworkCore;
using SiNet.Application.Billing;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Billing;

public sealed class SqlBillingPreparationStore(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IBillingPreparationStore
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<BillingPreparationRequestRecord?> GetActiveByMasterPlanProjectIdAsync(
        int masterPlanProjectId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var completed = (int)BillingPreparationStatus.Completed;
        var cancelled = (int)BillingPreparationStatus.Cancelled;
        var row = await Query(db)
            .Where(r => r.MasterPlanProjectId == masterPlanProjectId
                        && r.Status != completed
                        && r.Status != cancelled)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<BillingPreparationRequestRecord?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await Query(db)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<BillingPreparationRequestRecord?> GetByTaskIdAsync(
        int taskId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await Query(db)
            .FirstOrDefaultAsync(r => r.TaskId == taskId, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<BillingPreparationRequestRecord>> ListActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var completed = (int)BillingPreparationStatus.Completed;
        var cancelled = (int)BillingPreparationStatus.Cancelled;
        var rows = await Query(db)
            .Where(r => r.Status != completed && r.Status != cancelled)
            .OrderByDescending(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<BillingPreparationRequestRecord>> ListAwaitingConfirmationAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var awaiting = (int)BillingPreparationStatus.AwaitingMasterPlanConfirmation;
        var rows = await Query(db)
            .Where(r => r.Status == awaiting)
            .OrderByDescending(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    public async Task<BillingPreparationRequestRecord> InsertAsync(
        BillingPreparationRequestRecord request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = ToEntity(request);
        db.BillingPreparationRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<BillingPreparationRequestRecord> UpdateAsync(
        BillingPreparationRequestRecord request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Id <= 0)
            throw new InvalidOperationException("Cannot update unsaved preparation request.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await Query(db)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"בקשת הכנה {request.Id} לא נמצאה.");

        existing.MasterPlanProjectId = request.MasterPlanProjectId;
        existing.SiNetProjectId = request.SiNetProjectId;
        existing.ProjectNumber = request.ProjectNumber;
        existing.ProjectName = request.ProjectName;
        existing.CustomerName = request.CustomerName;
        existing.Status = (int)request.Status;
        existing.CreatedAtUtc = request.CreatedAtUtc;
        existing.CreatedByUserId = request.CreatedByUserId;
        existing.CreatedByLogin = request.CreatedByLogin;
        existing.ApprovedAtUtc = request.ApprovedAtUtc;
        existing.ApprovedByUserId = request.ApprovedByUserId;
        existing.ApprovedByLogin = request.ApprovedByLogin;
        existing.SnapshotTimestampUtc = request.SnapshotTimestampUtc;
        existing.LatestMasterPlanBackupUtc = request.LatestMasterPlanBackupUtc;
        existing.TaskId = request.TaskId;
        existing.ManualOverride = request.ManualOverride;
        existing.ManualOverrideReason = request.ManualOverrideReason;
        existing.ManualOverrideAtUtc = request.ManualOverrideAtUtc;
        existing.ManualOverrideByUserId = request.ManualOverrideByUserId;
        existing.PricingStageTotal = request.PricingStageTotal;
        existing.PricingHoursTotal = request.PricingHoursTotal;
        existing.PricingTotal = request.PricingTotal;
        existing.PricingIsPartial = request.PricingIsPartial;
        existing.PricingFrozenAtUtc = request.PricingFrozenAtUtc;
        existing.PricingSourceSnapshotUtc = request.PricingSourceSnapshotUtc;
        existing.PricingFormulaVersion = request.PricingFormulaVersion;

        db.BillingPreparationHoursReports.RemoveRange(existing.Hours.SelectMany(h => h.Reports));
        db.BillingPreparationHoursLines.RemoveRange(existing.Hours);
        db.BillingPreparationStageLines.RemoveRange(existing.Stages);
        existing.Stages.Clear();
        existing.Hours.Clear();
        foreach (var stage in request.Stages)
            existing.Stages.Add(ToStage(request.Id, stage));
        foreach (var hours in request.Hours)
            existing.Hours.Add(ToHours(request.Id, hours));

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(existing);
    }

    public async Task<IReadOnlyList<int>> FindHourReportIdsInOtherRequestsAsync(
        IReadOnlyList<int> hourReportIds,
        int? excludeRequestId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hourReportIds);
        if (hourReportIds.Count == 0)
            return [];

        var ids = hourReportIds.Distinct().ToList();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var cancelled = (int)BillingPreparationStatus.Cancelled;
        var hits = await db.BillingPreparationHoursReports
            .AsNoTracking()
            .Where(r => ids.Contains(r.HoursReportId))
            .Where(r => r.HoursLine.Request.Status != cancelled)
            .Where(r => excludeRequestId == null || r.HoursLine.RequestId != excludeRequestId.Value)
            .Select(r => r.HoursReportId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return hits;
    }

    private static IQueryable<BillingPreparationRequest> Query(SiNetSQLDbContext db) =>
        db.BillingPreparationRequests
            .Include(r => r.Stages)
            .Include(r => r.Hours)
                .ThenInclude(h => h.Reports);

    private static BillingPreparationRequest ToEntity(BillingPreparationRequestRecord request)
    {
        var entity = new BillingPreparationRequest
        {
            MasterPlanProjectId = request.MasterPlanProjectId,
            SiNetProjectId = request.SiNetProjectId,
            ProjectNumber = request.ProjectNumber,
            ProjectName = request.ProjectName,
            CustomerName = request.CustomerName,
            Status = (int)request.Status,
            CreatedAtUtc = request.CreatedAtUtc,
            CreatedByUserId = request.CreatedByUserId,
            CreatedByLogin = request.CreatedByLogin,
            ApprovedAtUtc = request.ApprovedAtUtc,
            ApprovedByUserId = request.ApprovedByUserId,
            ApprovedByLogin = request.ApprovedByLogin,
            SnapshotTimestampUtc = request.SnapshotTimestampUtc,
            LatestMasterPlanBackupUtc = request.LatestMasterPlanBackupUtc,
            TaskId = request.TaskId,
            ManualOverride = request.ManualOverride,
            ManualOverrideReason = request.ManualOverrideReason,
            ManualOverrideAtUtc = request.ManualOverrideAtUtc,
            ManualOverrideByUserId = request.ManualOverrideByUserId,
            PricingStageTotal = request.PricingStageTotal,
            PricingHoursTotal = request.PricingHoursTotal,
            PricingTotal = request.PricingTotal,
            PricingIsPartial = request.PricingIsPartial,
            PricingFrozenAtUtc = request.PricingFrozenAtUtc,
            PricingSourceSnapshotUtc = request.PricingSourceSnapshotUtc,
            PricingFormulaVersion = request.PricingFormulaVersion
        };
        foreach (var stage in request.Stages)
            entity.Stages.Add(ToStage(0, stage));
        foreach (var hours in request.Hours)
            entity.Hours.Add(ToHours(0, hours));
        return entity;
    }

    private static BillingPreparationStageLine ToStage(int requestId, BillingPreparationStageLineSnapshot stage) =>
        new()
        {
            RequestId = requestId,
            MasterPlanStageId = stage.MasterPlanStageId,
            MasterPlanSubContractId = stage.MasterPlanSubContractId,
            StageName = stage.StageName,
            SubContractName = stage.SubContractName,
            StageWeightWithinSubContract = stage.StageWeightWithinSubContract,
            ObservedCumulativeProgress = stage.ObservedCumulativeProgress,
            TargetCumulativeProgress = stage.TargetCumulativeProgress,
            RequestedDelta = stage.RequestedDelta,
            HasDataQualityFlag = stage.HasDataQualityFlag,
            SnapshotTimestampUtc = stage.SnapshotTimestampUtc,
            ConfirmationMode = (int)stage.ConfirmationMode,
            ConfirmedAtUtc = stage.ConfirmedAtUtc,
            ConfirmedByUserId = stage.ConfirmedByUserId,
            ConfirmationNote = stage.ConfirmationNote,
            PricingBaseAmount = stage.PricingBaseAmount,
            PricingDiscountFraction = stage.PricingDiscountFraction,
            PricingCalculatedAmount = stage.PricingCalculatedAmount,
            PricingUnavailableReason = stage.PricingUnavailableReason
        };

    private static BillingPreparationHoursLine ToHours(int requestId, BillingPreparationHoursLineSnapshot hours)
    {
        var line = new BillingPreparationHoursLine
        {
            RequestId = requestId,
            MasterPlanSubContractId = hours.MasterPlanSubContractId,
            SubContractName = hours.SubContractName,
            FromDate = hours.FromDate.Date,
            ToDate = hours.ToDate.Date,
            ReportCount = hours.ReportCount,
            TotalHours = hours.TotalHours,
            OverlappingHourReportIds = string.Join(",", hours.OverlappingHourReportIds),
            SnapshotTimestampUtc = hours.SnapshotTimestampUtc,
            ConfirmationMode = (int)hours.ConfirmationMode,
            ConfirmedAtUtc = hours.ConfirmedAtUtc,
            ConfirmedByUserId = hours.ConfirmedByUserId,
            ConfirmationNote = hours.ConfirmationNote,
            PricingHourlyRate = hours.PricingHourlyRate,
            PricingDiscountFraction = hours.PricingDiscountFraction,
            PricingCalculatedAmount = hours.PricingCalculatedAmount,
            PricingUnavailableReason = hours.PricingUnavailableReason
        };
        foreach (var report in hours.Reports)
        {
            line.Reports.Add(new BillingPreparationHoursReport
            {
                HoursReportId = report.HoursReportId,
                Date = report.Date.Date,
                EmployeeId = report.EmployeeId,
                EmployeeName = report.EmployeeName,
                Hours = report.Hours,
                SubContractId = report.SubContractId,
                SubContractStepId = report.SubContractStepId,
                Description = report.Description
            });
        }

        return line;
    }

    private static BillingPreparationRequestRecord Map(BillingPreparationRequest row) =>
        new(
            row.Id,
            row.MasterPlanProjectId,
            row.SiNetProjectId,
            row.ProjectNumber,
            row.ProjectName,
            row.CustomerName,
            (BillingPreparationStatus)row.Status,
            row.CreatedAtUtc,
            row.CreatedByUserId,
            row.CreatedByLogin,
            row.ApprovedAtUtc,
            row.ApprovedByUserId,
            row.ApprovedByLogin,
            row.SnapshotTimestampUtc,
            row.LatestMasterPlanBackupUtc,
            row.TaskId,
            row.ManualOverride,
            row.ManualOverrideReason,
            row.ManualOverrideAtUtc,
            row.ManualOverrideByUserId,
            row.Stages
                .OrderBy(s => s.Id)
                .Select(s => new BillingPreparationStageLineSnapshot(
                    s.MasterPlanStageId,
                    s.MasterPlanSubContractId,
                    s.StageName,
                    s.SubContractName,
                    s.StageWeightWithinSubContract,
                    s.ObservedCumulativeProgress,
                    s.TargetCumulativeProgress,
                    s.RequestedDelta,
                    s.HasDataQualityFlag,
                    s.SnapshotTimestampUtc,
                    (BillingConfirmationMode)s.ConfirmationMode,
                    s.ConfirmedAtUtc,
                    s.ConfirmedByUserId,
                    s.ConfirmationNote,
                    s.PricingBaseAmount,
                    s.PricingDiscountFraction,
                    s.PricingCalculatedAmount,
                    s.PricingUnavailableReason))
                .ToList(),
            row.Hours
                .OrderBy(h => h.Id)
                .Select(h => new BillingPreparationHoursLineSnapshot(
                    h.MasterPlanSubContractId,
                    h.SubContractName,
                    h.FromDate,
                    h.ToDate,
                    h.ReportCount,
                    h.TotalHours,
                    h.Reports
                        .OrderBy(r => r.Date)
                        .ThenBy(r => r.HoursReportId)
                        .Select(r => new BillingPreparationHourReportSnapshot(
                            r.HoursReportId,
                            r.Date,
                            r.EmployeeId,
                            r.EmployeeName,
                            r.Hours,
                            r.SubContractId,
                            r.SubContractStepId,
                            r.Description))
                        .ToList(),
                    ParseIds(h.OverlappingHourReportIds),
                    h.SnapshotTimestampUtc,
                    (BillingConfirmationMode)h.ConfirmationMode,
                    h.ConfirmedAtUtc,
                    h.ConfirmedByUserId,
                    h.ConfirmationNote,
                    h.PricingHourlyRate,
                    h.PricingDiscountFraction,
                    h.PricingCalculatedAmount,
                    h.PricingUnavailableReason))
                .ToList(),
            row.PricingStageTotal,
            row.PricingHoursTotal,
            row.PricingTotal,
            row.PricingIsPartial,
            row.PricingFrozenAtUtc,
            row.PricingSourceSnapshotUtc,
            row.PricingFormulaVersion);

    private static IReadOnlyList<int> ParseIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();
    }
}
