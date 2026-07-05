using Microsoft.EntityFrameworkCore;
using SiNet.Application.DevTools;
using SiNet.Application.Identity;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>
/// New System dev-database reset — behavior ported from legacy <c>DevDataResetService</c>.
/// DEBUG-only; Release throws <see cref="NotSupportedException"/>.
/// </summary>
public sealed class SqlDevDataResetService : IDevDataResetService
{
    private static readonly string[] TablesToWipe =
    [
        "EmailInboxAttachment", "EmailInboxMessage",
        "ProjectAccMapping", "AccSystemResource", "AccHub",
        "Sync_RunFailures",
        "SystemSettings",
        "ProjectAssignmentEvent", "ProjectAssignmentStatus", "UserSetting",
        "ProjectTypeTaskType", "ProjectTypeStatus", "UserStatusPreference",
        "TaskStatusToProjectStatusMapping", "TaskLink", "TaskCompletionRules",
        "TaskTriggerRules", "TaskBehaviorDefinitions", "TaskType",
        "ProjectAssignment_ProjectAssignment", "ProjectAssignment",
        "DecisionHistory", "ProjectDecision", "DecisionCategory",
        "InspectionSeriesFileConfigs", "InspectionReportDrawings", "InspectionReportSnapshots",
        "InspectionReportReviewedFiles", "InspectionNoteAttachments", "InspectionNotes",
        "InspectionNoteStatuses", "InspectionSeries", "CommentsBank", "Sections",
        "SectionNames", "ChapterNames", "Chapters", "InspectionReports",
        "WorkflowStageTransition", "WorkflowStartTrigger", "WorkflowTransitionAction",
        "WorkflowTransitionRule", "WorkflowStageTask", "WorkflowInstance",
        "ProjectTypeWorkflowDefinition", "WorkflowStageDefinition", "WorkflowDefinition",
        "ActionPermission", "ProjectAlternative",
        "UserGroupMemberships", "UserGroups",
        "ProjectTypeDiscipline", "ProjectTypeWorkflowStage", "TaskResultDefinition",
        "ThreadStatusMapping",
    ];

    private static readonly HashSet<string> UserSettingsTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "UserGroupMemberships", "UserGroups", "UserSetting", "UserStatusPreference",
    };

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IStaticSeedService _seedService;
    private readonly DevToolsGate _gate;

    public SqlDevDataResetService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IStaticSeedService seedService,
        DevToolsGate gate)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _seedService = seedService ?? throw new ArgumentNullException(nameof(seedService));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public string CurrentWindowsUser => DevToolsWindowsUserPolicy.CurrentWindowsUser;

    public bool IsCurrentUserAllowed() => DevToolsWindowsUserPolicy.IsCurrentUserAllowed();

    public async ValueTask<string?> PeekDatabaseNameAsync(CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return ctx.Database.GetDbConnection().Database;
    }

    public async ValueTask<DevDataResetResult> ResetAsync(DevDataResetOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        _gate.EnsureDevToolsAuthorized("Dev data reset");

        var errors = new List<string>();
        var tableResults = new List<DevDataResetTableResult>();
        var started = DateTime.UtcNow;

        await using var ctx = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var databaseName = ctx.Database.GetDbConnection().Database;

        DevToolsLog.Info(
            $"[DevDataReset] User='{CurrentWindowsUser}' Database='{databaseName}' " +
            $"PreserveSystemSettings={options.PreserveSystemSettings} ResetUserSettings={options.ResetUserSettings}");

        await ctx.Database.ExecuteSqlRawAsync(
            "EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'", ct).ConfigureAwait(false);

        string? postResetError = null;
        try
        {
            foreach (var table in TablesToWipe)
            {
                ct.ThrowIfCancellationRequested();

                if (options.PreserveSystemSettings &&
                    string.Equals(table, "SystemSettings", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!options.ResetUserSettings && UserSettingsTables.Contains(table))
                    continue;

                try
                {
                    var exists = await ctx.Database
                        .SqlQueryRaw<int>(
                            $"SELECT CASE WHEN OBJECT_ID(N'dbo.[{table}]', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]")
                        .ToListAsync(ct)
                        .ConfigureAwait(false);

                    if (exists.Count == 0 || exists[0] == 0)
                        continue;

                    var rows = await ctx.Database.ExecuteSqlRawAsync($"DELETE FROM [{table}]", ct)
                        .ConfigureAwait(false);

                    var hasIdentity = await ctx.Database
                        .SqlQueryRaw<int>(
                            $"SELECT CASE WHEN OBJECTPROPERTY(OBJECT_ID(N'dbo.[{table}]'), 'TableHasIdentity') = 1 THEN 1 ELSE 0 END AS [Value]")
                        .ToListAsync(ct)
                        .ConfigureAwait(false);

                    if (hasIdentity.Count > 0 && hasIdentity[0] == 1)
                    {
                        try
                        {
                            await ctx.Database.ExecuteSqlRawAsync(
                                $"DBCC CHECKIDENT ('[{table}]', RESEED, 0)", ct).ConfigureAwait(false);
                        }
                        catch (Exception reseedEx)
                        {
                            DevToolsLog.Warn($"[DevDataReset] Reseed skipped for '{table}': {reseedEx.Message}");
                        }
                    }

                    tableResults.Add(new DevDataResetTableResult(table, rows, null));
                }
                catch (Exception ex)
                {
                    tableResults.Add(new DevDataResetTableResult(table, 0, ex.Message));
                    errors.Add($"{table}: {ex.Message}");
                    DevToolsLog.Error(ex, $"[DevDataReset] Failed to wipe '{table}'");
                }
            }
        }
        finally
        {
            try
            {
                await ctx.Database.ExecuteSqlRawAsync(
                    "EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL'",
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                postResetError = ex.Message;
                errors.Add($"FK re-enable: {ex.Message}");
                DevToolsLog.Error(ex, "[DevDataReset] Failed to re-enable FK constraints");
            }
        }

        var seedApplied = false;
        string? seedError = null;
        var mappingsApplied = false;
        string? mappingsError = null;
        var workflowSeedApplied = false;
        string? workflowSeedError = null;
        var demoTasksSeedApplied = false;
        string? demoTasksSeedError = null;

        if (options.IncludeTaskSeed || options.IncludeMappingsSeed || options.IncludeWorkflowSeed || options.IncludeDemoTasks)
        {
            _seedService.ResetSeedingSessionFlag();
        }

        if (options.IncludeTaskSeed)
        {
            var seed = await _seedService.SeedTaskStaticLookupsAsync(ct).ConfigureAwait(false);
            seedApplied = seed.Succeeded;
            if (!seed.Succeeded)
            {
                seedError = string.Join("; ", seed.Errors);
                errors.AddRange(seed.Errors);
            }
        }

        if (options.IncludeMappingsSeed)
        {
            var map = await _seedService.SeedTaskMappingsAsync(ct).ConfigureAwait(false);
            mappingsApplied = map.Succeeded;
            if (!map.Succeeded)
            {
                mappingsError = string.Join("; ", map.Errors);
                errors.AddRange(map.Errors);
            }
        }

        if (options.IncludeWorkflowSeed)
        {
            var wf = await _seedService.SeedWorkflowDefinitionsAsync(ct).ConfigureAwait(false);
            workflowSeedApplied = wf.Succeeded;
            if (!wf.Succeeded)
            {
                workflowSeedError = string.Join("; ", wf.Errors);
                errors.AddRange(wf.Errors);
            }
        }

        if (options.IncludeDemoTasks)
        {
            var demo = await _seedService.SeedDemoTasksAsync(ct).ConfigureAwait(false);
            demoTasksSeedApplied = demo.Succeeded;
            if (!demo.Succeeded)
            {
                demoTasksSeedError = string.Join("; ", demo.Errors);
                errors.AddRange(demo.Errors);
            }
        }

        return new DevDataResetResult
        {
            WindowsUser = CurrentWindowsUser,
            DatabaseName = databaseName,
            StartedUtc = started,
            CompletedUtc = DateTime.UtcNow,
            Tables = tableResults,
            PostResetError = postResetError,
            SeedApplied = seedApplied,
            SeedError = seedError,
            MappingsApplied = mappingsApplied,
            MappingsError = mappingsError,
            WorkflowSeedApplied = workflowSeedApplied,
            WorkflowSeedError = workflowSeedError,
            DemoTasksSeedApplied = demoTasksSeedApplied,
            DemoTasksSeedError = demoTasksSeedError,
            SystemSettingsPreserved = options.PreserveSystemSettings,
            UserSettingsPreserved = !options.ResetUserSettings,
            Errors = errors,
        };
    }
}
