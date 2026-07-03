using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Settings;

/// <summary>
/// Reads/writes global <c>Logging.*</c> rows in <c>SystemSettings</c> (read-only queries use
/// <c>AsNoTracking</c>; writes require admin authorization).
/// </summary>
public sealed class SqlLoggingSettingsService
    : ILoggingSettingsQueryService, ILoggingSettingsCommandService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IAuthorizationQueryService _authorization;

    public SqlLoggingSettingsService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IAuthorizationQueryService authorization)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public async Task<CentralLoggingSettingsDto> GetCentralLoggingAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await context.SystemSettings
            .AsNoTracking()
            .Where(s => LoggingSettingKeys.All.Contains(s.SettingKey))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return MapToDto(rows);
    }

    public async Task SaveCentralLoggingAsync(
        CentralLoggingSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await RequireSystemSettingsWriteAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var updates = ToKeyValuePairs(settings);
        foreach (var (key, value) in updates)
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

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task RequireSystemSettingsWriteAsync(CancellationToken cancellationToken)
    {
        if (!await _authorization
                .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.SystemSettingsWrite, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException("Administrator access required for system logging settings.");
        }
    }

    internal static CentralLoggingSettingsDto MapToDto(IReadOnlyList<SystemSetting> rows)
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

    internal static IReadOnlyList<(string Key, string Value)> ToKeyValuePairs(CentralLoggingSettingsDto settings)
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

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) ? parsed : fallback;

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
