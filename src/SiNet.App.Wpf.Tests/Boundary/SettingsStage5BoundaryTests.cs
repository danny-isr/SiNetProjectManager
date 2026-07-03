using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Services.Settings;
using SiNetProjectManagerV2.Services.Composition;
using SiNetSQL.Data;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Stage 5 settings guards — see <c>docs/SETTINGS.md</c>.
/// </summary>
public sealed class SettingsStage5BoundaryTests
{
    private static readonly string[] ForbiddenLegacySettingsInAppWpf =
    [
        "SettingsManager",
        "AppSettings",
        "CentralLoggingSettings",
        "SiNetSQL.Services.AppLogger",
        "AppLogger.Configure",
        "SystemSettingKeys",
        "SiNetSQL.Services.Logging",
    ];

    [Fact]
    public void AddSiNetUserLoggingSettings_resolves_JsonUserLoggingSettingsService()
    {
        var services = new ServiceCollection();
        services.AddSiNetUserLoggingSettings();

        var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<IAppSettingsService>();

        Assert.IsType<JsonUserLoggingSettingsService>(settings);
    }

    [Fact]
    public void AddSiNetLoggingSettingsSql_resolves_SqlLoggingSettingsService_for_query_and_command()
    {
        var services = new ServiceCollection();
        RegisterSqlLoggingSettingsDependencies(services);
        services.AddSiNetLoggingSettingsSql();

        var provider = services.BuildServiceProvider();

        var query = provider.GetRequiredService<ILoggingSettingsQueryService>();
        var command = provider.GetRequiredService<ILoggingSettingsCommandService>();

        Assert.IsType<SqlLoggingSettingsService>(query);
        Assert.IsType<SqlLoggingSettingsService>(command);
        Assert.Same(query, command);
    }

    [Fact]
    public void New_system_settings_slice_resolves_LegacyLoggingRuntimeApplier()
    {
        var services = new ServiceCollection();
        services.AddSiNetUserLoggingSettings();
        RegisterSqlLoggingSettingsDependencies(services);
        services.AddSiNetLoggingSettingsSql();
        services.AddSingleton<ILoggingRuntimeApplier, LegacyLoggingRuntimeApplier>();

        var provider = services.BuildServiceProvider();

        Assert.IsType<JsonUserLoggingSettingsService>(provider.GetRequiredService<IAppSettingsService>());
        Assert.IsType<SqlLoggingSettingsService>(provider.GetRequiredService<ILoggingSettingsQueryService>());
        Assert.IsType<LegacyLoggingRuntimeApplier>(provider.GetRequiredService<ILoggingRuntimeApplier>());
    }

    [Fact]
    public void Settings_target_doc_exists_and_describes_ports()
    {
        var doc = File.ReadAllText(SettingsDocPath);
        Assert.Contains("IAppSettingsService", doc, StringComparison.Ordinal);
        Assert.Contains("ILoggingSettingsQueryService", doc, StringComparison.Ordinal);
        Assert.Contains("ILoggingRuntimeApplier", doc, StringComparison.Ordinal);
        Assert.Contains("settings.json", doc, StringComparison.Ordinal);
        Assert.Contains("AddSiNetUserLoggingSettings", doc, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> AppWpfSourceFiles()
    {
        foreach (var file in EnumerateAppWpfSourceFiles())
        {
            yield return [Path.GetRelativePath(AppWpfRoot, file)];
        }
    }

    [Theory]
    [MemberData(nameof(AppWpfSourceFiles))]
    public void App_Wpf_source_does_not_reference_legacy_settings(string relativePath)
    {
        var content = File.ReadAllText(Path.Combine(AppWpfRoot, relativePath));

        foreach (var forbidden in ForbiddenLegacySettingsInAppWpf)
        {
            Assert.False(
                content.Contains(forbidden, StringComparison.Ordinal),
                $"Forbidden settings reference '{forbidden}' in src/SiNet.App.Wpf/{relativePath}");
        }
    }

    [Fact]
    public void JsonUserLoggingSettingsService_round_trips_logging_fields()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sinet-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");

        try
        {
            File.WriteAllText(
                settingsPath,
                """
                {
                  "fontSize": 12,
                  "loggingEnabled": true,
                  "logDirectory": "C:\\Custom\\Logs"
                }
                """);

            var (enabled, directory) = JsonUserLoggingSettingsService.ReadLoggingFields(settingsPath);
            Assert.True(enabled);
            Assert.Equal("C:\\Custom\\Logs", directory);

            JsonUserLoggingSettingsService.WriteLoggingFields(settingsPath, false, null);

            var merged = File.ReadAllText(settingsPath);
            Assert.Contains("\"fontSize\": 12", merged, StringComparison.Ordinal);
            Assert.Contains("\"loggingEnabled\": false", merged, StringComparison.Ordinal);

            var dto = JsonUserLoggingSettingsService.CreateDto(false, null);
            Assert.Equal(LoggingSettingsPaths.BootstrapDefaultLocalLogDirectory, dto.EffectiveLocalLogDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SqlLoggingSettingsService_maps_db_rows_to_dto()
    {
        var rows = new List<SiNetSQL.Models.SystemSetting>
        {
            new() { SettingKey = LoggingSettingKeys.CentralLogPath, SettingValue = @"\\server\logs" },
            new() { SettingKey = LoggingSettingKeys.LocalRetentionDays, SettingValue = "30" },
            new() { SettingKey = LoggingSettingKeys.ClientFileLevel, SettingValue = "Debug" },
        };

        var dto = SqlLoggingSettingsService.MapToDto(rows);

        Assert.Equal(@"\\server\logs", dto.CentralLogPath);
        Assert.Equal(30, dto.LocalRetentionDays);
        Assert.Equal(LogLevelDto.Debug, dto.Client.FileLevel);
        Assert.True(dto.CentralLoggingEnabled);
    }

    private static void RegisterSqlLoggingSettingsDependencies(IServiceCollection services)
    {
        var auth = new Mock<IAuthorizationQueryService>();
        auth.Setup(a => a.CanCurrentUserAccessFeatureAsync(
                AppFeatureCodes.SystemSettingsWrite,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var dbFactory = new Mock<IDbContextFactory<SiNetSQLDbContext>>();
        dbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new SiNetSQLDbContext(
                new DbContextOptionsBuilder<SiNetSQLDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options));

        services.AddSingleton(auth.Object);
        services.AddSingleton(dbFactory.Object);
    }

    private static string RepoRoot => RepoPaths.RepoRoot;

    private static string AppWpfRoot => Path.Combine(RepoRoot, "src", "SiNet.App.Wpf");

    private static string SettingsDocPath => Path.Combine(RepoRoot, "docs", "SETTINGS.md");

    private static IEnumerable<string> EnumerateAppWpfSourceFiles()
    {
        if (!Directory.Exists(AppWpfRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(AppWpfRoot, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            yield return file;
        }
    }
}
