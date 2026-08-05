using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Settings;

/// <summary>
/// Reads/writes all managed <c>SystemSettings</c> rows including <c>Logging.*</c>.
/// Also implements the Stage 5 logging-specific ports for backward compatibility.
/// </summary>
public sealed class SqlSystemSettingsService
    : ISystemSettingsQueryService,
      ISystemSettingsCommandService,
      ILoggingSettingsQueryService,
      ILoggingSettingsCommandService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IAuthorizationQueryService _authorization;

    public SqlSystemSettingsService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IAuthorizationQueryService authorization)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public async Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await context.SystemSettings
            .AsNoTracking()
            .Where(s => SystemSettingKeys.AllManaged.Contains(s.SettingKey))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return MapToSystemDto(rows);
    }

    public async Task SaveSystemSettingsAsync(
        SystemSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await RequireSystemSettingsWriteAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (key, value) in ToKeyValuePairs(settings))
        {
            await UpsertAsync(context, key, value, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CentralLoggingSettingsDto> GetCentralLoggingAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        return all.Logging;
    }

    public Task SaveCentralLoggingAsync(
        CentralLoggingSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SavePartialAsync(ToLoggingKeyValuePairs(settings), cancellationToken);
    }

    public Task<bool> ProbeCentralLogPathAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(false);
        }

        try
        {
            var fullPath = path.Trim();
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            var probeFile = Path.Combine(fullPath, $".sinet-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probeFile, "probe");
            File.Delete(probeFile);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    internal static SystemSettingsDto MapToSystemDto(IReadOnlyList<SystemSetting> rows)
    {
        var map = rows.ToDictionary(r => r.SettingKey, r => r.SettingValue, StringComparer.Ordinal);
        string Get(string key, string fallback) =>
            map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

        var centralPath = map.TryGetValue(LoggingSettingKeys.CentralLogPath, out var cp) ? cp : null;

        return new SystemSettingsDto(
            new EmailOfficeSystemSettingsDto(
                Get(SystemSettingKeys.DefaultProjectTitle, SystemSettingsDefaults.DefaultProjectTitle),
                Get(SystemSettingKeys.OfficeManagementProjectId, SystemSettingsDefaults.OfficeManagementProjectId),
                Get(SystemSettingKeys.HourPriceDefault, SystemSettingsDefaults.HourPriceDefault),
                Get(SystemSettingKeys.InboxFolderName, SystemSettingsDefaults.InboxFolderNameFallback),
                map.TryGetValue(SystemSettingKeys.InboxProjectName, out var ipn) ? ipn : null,
                ParseInt(Get(SystemSettingKeys.AccViewerMaxTabs, SystemSettingsDefaults.AccViewerMaxTabs), 10),
                ParseBool(
                    Get(
                        SystemSettingKeys.EmailAutoSyncProjectLabelNames,
                        SystemSettingsDefaults.EmailAutoSyncProjectLabelNames ? "true" : "false"),
                    SystemSettingsDefaults.EmailAutoSyncProjectLabelNames)),
            new AccSystemSettingsDto(
                Get(SystemSettingKeys.AccServiceBaseUrl, string.Empty),
                Get(SystemSettingKeys.AccServicePinnedCertificateThumbprints, string.Empty),
                Get(SystemSettingKeys.AccBootstrapAdminEmail, string.Empty),
                Get(SystemSettingKeys.AccProjectTemplateName, string.Empty),
                Get(SystemSettingKeys.AccManualUploadAllowedExtensions, SystemSettingsDefaults.AccManualUploadAllowedExtensions)),
            new InspectionSystemSettingsDto(
                Get(SystemSettingKeys.InspectionTemplatesFolderId, string.Empty),
                Get(SystemSettingKeys.InspectionReportsFolderId, string.Empty),
                Get(SystemSettingKeys.ReportsOutputRoot, string.Empty),
                Get(SystemSettingKeys.StampTemplatePath, string.Empty)),
            new InspectionStatusLabelsDto(
                Get(SystemSettingKeys.StatusLabelPassed, SystemSettingsDefaults.StatusLabelPassed),
                Get(SystemSettingKeys.StatusLabelFailed, SystemSettingsDefaults.StatusLabelFailed),
                Get(SystemSettingKeys.StatusLabelRecurringFailed, SystemSettingsDefaults.StatusLabelRecurringFailed),
                Get(SystemSettingKeys.StatusLabelNotApplicable, SystemSettingsDefaults.StatusLabelNotApplicable)),
            new AiSystemSettingsDto(
                Get(SystemSettingKeys.OllamaBaseUrl, SystemSettingsDefaults.OllamaBaseUrl),
                Get(SystemSettingKeys.OllamaModel, SystemSettingsDefaults.OllamaModel),
                ReadAiLevel(map, SystemSettingKeys.AiModelSimple, SystemSettingKeys.AiProviderSimple),
                ReadAiLevel(map, SystemSettingKeys.AiModelQualityCheck, SystemSettingKeys.AiProviderQualityCheck),
                ReadAiLevel(map, SystemSettingKeys.AiModelWriting, SystemSettingKeys.AiProviderWriting),
                ReadAiLevel(map, SystemSettingKeys.AiModelDeepAnalysis, SystemSettingKeys.AiProviderDeepAnalysis),
                Get(SystemSettingKeys.AiConfiguredCloudModels, string.Empty)),
            MapLoggingDto(rows),
            new WorkflowSystemSettingsDto(
                Math.Max(1, ParseInt(
                    Get(SystemSettingKeys.WorkflowMaxOpenChildInstances,
                        SystemSettingsDefaults.WorkflowMaxOpenChildInstances.ToString()),
                    SystemSettingsDefaults.WorkflowMaxOpenChildInstances))),
            new ProjectWorkSystemSettingsDto(
                Get(SystemSettingKeys.ProjectWorkScanExclusionRules,
                    SystemSettingsDefaults.ProjectWorkScanExclusionRules)),
            new DiagnosticsSystemSettingsDto(
                Get(SystemSettingKeys.DiagnosticsCrashReportSharePath, string.Empty),
                Get(SystemSettingKeys.DiagnosticsCrashAppFilters,
                    SystemSettingsDefaults.DiagnosticsCrashAppFilters),
                Math.Max(1, ParseInt(
                    Get(SystemSettingKeys.DiagnosticsCrashLookbackDays,
                        SystemSettingsDefaults.DiagnosticsCrashLookbackDays.ToString()),
                    SystemSettingsDefaults.DiagnosticsCrashLookbackDays)),
                Math.Max(1, ParseInt(
                    Get(SystemSettingKeys.DiagnosticsCrashReportRetentionDays,
                        SystemSettingsDefaults.DiagnosticsCrashReportRetentionDays.ToString()),
                    SystemSettingsDefaults.DiagnosticsCrashReportRetentionDays))));
    }

    internal static CentralLoggingSettingsDto MapLoggingDto(IReadOnlyList<SystemSetting> rows)
    {
        var map = rows.ToDictionary(r => r.SettingKey, r => r.SettingValue, StringComparer.Ordinal);
        string? Get(string key) => map.TryGetValue(key, out var v) ? v : null;
        var centralPath = Get(LoggingSettingKeys.CentralLogPath);

        return new CentralLoggingSettingsDto(
            string.IsNullOrWhiteSpace(centralPath) ? null : centralPath.Trim(),
            ParseInt(Get(LoggingSettingKeys.LocalRetentionDays), 14),
            ParseInt(Get(LoggingSettingKeys.CentralRetentionDays), 90),
            new AppLogLevelsDto(
                ParseLevel(Get(LoggingSettingKeys.ClientFileLevel), LogLevelDto.Error),
                ParseLevel(Get(LoggingSettingKeys.ClientCentralLevel), LogLevelDto.Warning)),
            new AppLogLevelsDto(
                ParseLevel(Get(LoggingSettingKeys.AccServiceFileLevel), LogLevelDto.Information),
                ParseLevel(Get(LoggingSettingKeys.AccServiceCentralLevel), LogLevelDto.Warning)),
            new AppLogLevelsDto(
                ParseLevel(Get(LoggingSettingKeys.SyncEngineFileLevel), LogLevelDto.Information),
                ParseLevel(Get(LoggingSettingKeys.SyncEngineCentralLevel), LogLevelDto.Warning)),
            !string.IsNullOrWhiteSpace(centralPath));
    }

    internal static IReadOnlyList<(string Key, string Value)> ToKeyValuePairs(SystemSettingsDto settings)
    {
        var pairs = new List<(string, string)>
        {
            (SystemSettingKeys.DefaultProjectTitle, settings.EmailOffice.DefaultProjectTitle.Trim()),
            (SystemSettingKeys.OfficeManagementProjectId, settings.EmailOffice.OfficeManagementProjectId.Trim()),
            (SystemSettingKeys.HourPriceDefault, settings.EmailOffice.HourPriceDefault.Trim()),
            (SystemSettingKeys.InboxFolderName, settings.EmailOffice.InboxFolderName.Trim()),
            (SystemSettingKeys.AccViewerMaxTabs, settings.EmailOffice.AccViewerMaxTabs.ToString()),
            (SystemSettingKeys.EmailAutoSyncProjectLabelNames,
                settings.EmailOffice.AutoSyncProjectLabelNames ? "true" : "false"),
            (SystemSettingKeys.AccServiceBaseUrl, settings.Acc.AccServiceBaseUrl.Trim()),
            (SystemSettingKeys.AccServicePinnedCertificateThumbprints, settings.Acc.AccServicePinnedCertificateThumbprints.Trim()),
            (SystemSettingKeys.AccBootstrapAdminEmail, settings.Acc.AccBootstrapAdminEmail.Trim()),
            (SystemSettingKeys.AccProjectTemplateName, settings.Acc.AccProjectTemplateName.Trim()),
            (SystemSettingKeys.AccManualUploadAllowedExtensions, settings.Acc.AccManualUploadAllowedExtensions.Trim()),
            (SystemSettingKeys.InspectionTemplatesFolderId, settings.Inspection.InspectionTemplatesFolderId.Trim()),
            (SystemSettingKeys.InspectionReportsFolderId, settings.Inspection.InspectionReportsFolderId.Trim()),
            (SystemSettingKeys.ReportsOutputRoot, settings.Inspection.ReportsOutputRoot.Trim()),
            (SystemSettingKeys.StampTemplatePath, settings.Inspection.StampTemplatePath.Trim()),
            (SystemSettingKeys.StatusLabelPassed, settings.StatusLabels.Passed.Trim()),
            (SystemSettingKeys.StatusLabelFailed, settings.StatusLabels.Failed.Trim()),
            (SystemSettingKeys.StatusLabelRecurringFailed, settings.StatusLabels.RecurringFailed.Trim()),
            (SystemSettingKeys.StatusLabelNotApplicable, settings.StatusLabels.NotApplicable.Trim()),
            (SystemSettingKeys.OllamaBaseUrl, settings.Ai.OllamaBaseUrl.Trim()),
            (SystemSettingKeys.OllamaModel, settings.Ai.OllamaModel.Trim()),
            (SystemSettingKeys.AiModelSimple, settings.Ai.Simple.Model.Trim()),
            (SystemSettingKeys.AiProviderSimple, settings.Ai.Simple.Provider.Trim()),
            (SystemSettingKeys.AiModelQualityCheck, settings.Ai.QualityCheck.Model.Trim()),
            (SystemSettingKeys.AiProviderQualityCheck, settings.Ai.QualityCheck.Provider.Trim()),
            (SystemSettingKeys.AiModelWriting, settings.Ai.Writing.Model.Trim()),
            (SystemSettingKeys.AiProviderWriting, settings.Ai.Writing.Provider.Trim()),
            (SystemSettingKeys.AiModelDeepAnalysis, settings.Ai.DeepAnalysis.Model.Trim()),
            (SystemSettingKeys.AiProviderDeepAnalysis, settings.Ai.DeepAnalysis.Provider.Trim()),
            (SystemSettingKeys.AiConfiguredCloudModels, settings.Ai.ConfiguredCloudModelsCsv.Trim()),
            (SystemSettingKeys.WorkflowMaxOpenChildInstances,
                Math.Max(1, settings.Workflow.MaxOpenChildInstances).ToString()),
            (SystemSettingKeys.ProjectWorkScanExclusionRules,
                settings.ProjectWork.ScanExclusionRules.Trim()),
            (SystemSettingKeys.DiagnosticsCrashReportSharePath,
                settings.Diagnostics.CrashReportSharePath.Trim()),
            (SystemSettingKeys.DiagnosticsCrashAppFilters,
                settings.Diagnostics.CrashAppFilters.Trim()),
            (SystemSettingKeys.DiagnosticsCrashLookbackDays,
                Math.Max(1, settings.Diagnostics.CrashLookbackDays).ToString()),
            (SystemSettingKeys.DiagnosticsCrashReportRetentionDays,
                Math.Max(1, settings.Diagnostics.CrashReportRetentionDays).ToString()),
        };

        if (!string.IsNullOrWhiteSpace(settings.EmailOffice.InboxProjectName))
        {
            pairs.Add((SystemSettingKeys.InboxProjectName, settings.EmailOffice.InboxProjectName.Trim()));
        }

        pairs.AddRange(ToLoggingKeyValuePairs(settings.Logging));
        return pairs;
    }

    internal static IReadOnlyList<(string Key, string Value)> ToLoggingKeyValuePairs(CentralLoggingSettingsDto settings)
        =>
        [
            (LoggingSettingKeys.CentralLogPath, settings.CentralLogPath?.Trim() ?? string.Empty),
            (LoggingSettingKeys.LocalRetentionDays, settings.LocalRetentionDays.ToString()),
            (LoggingSettingKeys.CentralRetentionDays, settings.CentralRetentionDays.ToString()),
            (LoggingSettingKeys.ClientFileLevel, FormatLevel(settings.Client.FileLevel)),
            (LoggingSettingKeys.ClientCentralLevel, FormatLevel(settings.Client.CentralLevel)),
            (LoggingSettingKeys.AccServiceFileLevel, FormatLevel(settings.AccService.FileLevel)),
            (LoggingSettingKeys.AccServiceCentralLevel, FormatLevel(settings.AccService.CentralLevel)),
            (LoggingSettingKeys.SyncEngineFileLevel, FormatLevel(settings.SyncEngine.FileLevel)),
            (LoggingSettingKeys.SyncEngineCentralLevel, FormatLevel(settings.SyncEngine.CentralLevel)),
        ];

    private async Task SavePartialAsync(
        IReadOnlyList<(string Key, string Value)> updates,
        CancellationToken cancellationToken)
    {
        await RequireSystemSettingsWriteAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (key, value) in updates)
        {
            await UpsertAsync(context, key, value, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertAsync(
        SiNetSQLDbContext context,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var row = await context.SystemSettings
            .FirstOrDefaultAsync(s => s.SettingKey == key, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            context.SystemSettings.Add(new SystemSetting
            {
                SettingKey = key,
                SettingValue = value,
                LastUpdated = DateTime.UtcNow,
            });
        }
        else
        {
            row.SettingValue = value;
            row.LastUpdated = DateTime.UtcNow;
        }
    }

    private async Task RequireSystemSettingsWriteAsync(CancellationToken cancellationToken)
    {
        if (!await _authorization
                .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.SystemSettingsWrite, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException("Administrator access required for system settings.");
        }
    }

    private static AiModelLevelSelectionDto ReadAiLevel(
        IReadOnlyDictionary<string, string> map,
        string modelKey,
        string providerKey)
        => new(
            map.TryGetValue(modelKey, out var model) ? model : string.Empty,
            map.TryGetValue(providerKey, out var provider) ? provider : string.Empty);

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) ? parsed : fallback;

    private static bool ParseBool(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (bool.TryParse(value.Trim(), out var parsed))
            return parsed;
        if (string.Equals(value.Trim(), "1", StringComparison.Ordinal)
            || string.Equals(value.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(value.Trim(), "0", StringComparison.Ordinal)
            || string.Equals(value.Trim(), "no", StringComparison.OrdinalIgnoreCase))
            return false;
        return fallback;
    }

    private static LogLevelDto ParseLevel(string? value, LogLevelDto fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Enum.TryParse<LogLevelDto>(value.Trim(), ignoreCase: true, out var level)
            ? level
            : fallback;
    }

    private static string FormatLevel(LogLevelDto level) => level.ToString();
}
