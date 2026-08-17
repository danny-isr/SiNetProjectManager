using Microsoft.EntityFrameworkCore;
using SiNet.Application.Settings;
using SiNet.Application.Workflow;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Loads Pilot.* from SystemSettings and applies <see cref="PilotStartPolicy"/> for root starts.
/// When <see cref="ISystemSettingsQueryService"/> is absent, evaluates as Pilot disabled (fail-closed).
/// </summary>
public sealed class SqlPilotStartGate : IPilotStartGate
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly ISystemSettingsQueryService? _systemSettings;

    public SqlPilotStartGate(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        ISystemSettingsQueryService? systemSettings = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _systemSettings = systemSettings;
    }

    public async Task EnsureRootStartAllowedAsync(
        int actingUserId,
        int workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        if (workflowDefinitionId <= 0)
            throw new WorkflowStartPreflightException("הפעלת תהליך חדש חסומה: מזהה הגדרת תהליך לא תקין.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var code = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Id == workflowDefinitionId)
            .Select(d => d.Code)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new WorkflowStartPreflightException(
                $"הפעלת תהליך חדש חסומה: הגדרת תהליך {workflowDefinitionId} לא נמצאה.");
        }

        var (allowed, reason) = await EvaluateAsync(actingUserId, code, cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new WorkflowStartPreflightException(reason ?? "הפעלת תהליך חדש חסומה על ידי מדיניות הפיילוט.");
    }

    public async Task<(bool Allowed, string? DenyReasonHebrew)> EvaluateAsync(
        int actingUserId,
        string workflowCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowCode);

        var workflow = await LoadWorkflowSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (PilotStartPolicy.IsRootStartAllowed(workflow, actingUserId, workflowCode, out var deny))
            return (true, null);

        return (false, deny);
    }

    private async Task<WorkflowSystemSettingsDto> LoadWorkflowSettingsAsync(CancellationToken cancellationToken)
    {
        if (_systemSettings is null)
        {
            return new WorkflowSystemSettingsDto(
                SystemSettingsDefaults.WorkflowMaxOpenChildInstances,
                PilotEnabled: false,
                PilotAllowedUserIds: string.Empty,
                PilotAllowedWorkflowCodes: string.Empty);
        }

        var settings = await _systemSettings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        return settings.Workflow;
    }
}
