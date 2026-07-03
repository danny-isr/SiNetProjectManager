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
        "SiNetProjectManagerV2.AppSettings",
        "SiNetSQL.Services.Logging.CentralLoggingSettings",
        "SiNetSQL.Services.AppLogger",
        "AppLogger.Configure",
        "SiNetSQL.Services.SystemSettingKeys",
        "SiNetSQL.Services.Logging",
        "SiNetProjectManagerV2.WPF_Window",
    ];

    [Fact]
    public void AddSiNetUserLoggingSettings_resolves_JsonAppSettingsService()
    {
        var services = new ServiceCollection();
        services.AddSiNetUserLoggingSettings();

        var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<IAppSettingsService>();

        Assert.IsType<JsonAppSettingsService>(settings);
    }

    [Fact]
    public void AddSiNetSystemSettingsSql_resolves_SqlSystemSettingsService_for_all_ports()
    {
        var services = new ServiceCollection();
        RegisterSqlSettingsDependencies(services);
        services.AddSiNetSystemSettingsSql();

        var provider = services.BuildServiceProvider();

        var query = provider.GetRequiredService<ILoggingSettingsQueryService>();
        var command = provider.GetRequiredService<ILoggingSettingsCommandService>();
        var systemQuery = provider.GetRequiredService<ISystemSettingsQueryService>();
        var systemCommand = provider.GetRequiredService<ISystemSettingsCommandService>();

        Assert.IsType<SqlSystemSettingsService>(query);
        Assert.IsType<SqlSystemSettingsService>(command);
        Assert.IsType<SqlSystemSettingsService>(systemQuery);
        Assert.IsType<SqlSystemSettingsService>(systemCommand);
        Assert.Same(query, systemQuery);
    }

    [Fact]
    public void New_system_settings_slice_resolves_LegacyLoggingRuntimeApplier()
    {
        var services = new ServiceCollection();
        services.AddSiNetUserLoggingSettings();
        RegisterSqlSettingsDependencies(services);
        services.AddSiNetSystemSettingsSql();
        services.AddSingleton<ILoggingRuntimeApplier, LegacyLoggingRuntimeApplier>();

        var provider = services.BuildServiceProvider();

        Assert.IsType<JsonAppSettingsService>(provider.GetRequiredService<IAppSettingsService>());
        Assert.IsType<SqlSystemSettingsService>(provider.GetRequiredService<ISystemSettingsQueryService>());
        Assert.IsType<LegacyLoggingRuntimeApplier>(provider.GetRequiredService<ILoggingRuntimeApplier>());
    }

    [Fact]
    public void Settings_target_doc_exists_and_describes_ports()
    {
        var doc = File.ReadAllText(SettingsDocPath);
        Assert.Contains("IAppSettingsService", doc, StringComparison.Ordinal);
        Assert.Contains("ISystemSettingsQueryService", doc, StringComparison.Ordinal);
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
    public void JsonAppSettingsService_round_trips_and_preserves_unknown_fields()
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
                  "customFutureField": "keep-me",
                  "LoggingEnabled": true,
                  "LogDirectory": "C:\\Custom\\Logs"
                }
                """);

            var dto = JsonAppSettingsService.ReadDto(settingsPath);
            Assert.True(dto.Logging.LoggingEnabled);
            Assert.Equal("C:\\Custom\\Logs", dto.Logging.LogDirectory);
            Assert.Equal(12, dto.Appearance.FontSize);

            var mergedDto = dto with
            {
                Logging = dto.Logging with { LoggingEnabled = false },
            };
            JsonAppSettingsService.WriteDto(settingsPath, mergedDto);

            var merged = File.ReadAllText(settingsPath);
            Assert.Contains("\"customFutureField\": \"keep-me\"", merged, StringComparison.Ordinal);
            Assert.Contains("\"LoggingEnabled\": false", merged, StringComparison.Ordinal);

            var defaultDto = JsonAppSettingsService.CreateDefaultDto();
            Assert.Equal(LoggingSettingsMetadata.BootstrapDefaultLocalLogDirectory, defaultDto.Logging.EffectiveLocalLogDirectory);
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
    public void JsonAppSettingsService_defaults_match_legacy_AppSettings()
    {
        var dto = JsonAppSettingsService.CreateDefaultDto();
        Assert.Equal(UserAppSettingsDefaults.FontFamily, dto.Appearance.FontFamily);
        Assert.Equal(UserAppSettingsDefaults.FontSize, dto.Appearance.FontSize);
        Assert.Equal(UserAppSettingsDefaults.AllowMultipleInstances, dto.Behavior.AllowMultipleInstances);
        Assert.Equal(UserAppSettingsDefaults.LoggingEnabled, dto.Logging.LoggingEnabled);
        Assert.Equal(UserAppSettingsDefaults.FloatingActiveOpacity, dto.FloatingOpacity.ActiveOpacity);
    }

    [Fact]
    public void SqlSystemSettingsService_maps_db_rows_to_dto()
    {
        var rows = new List<SiNetSQL.Models.SystemSetting>
        {
            new() { SettingKey = LoggingSettingKeys.CentralLogPath, SettingValue = @"\\server\logs" },
            new() { SettingKey = LoggingSettingKeys.LocalRetentionDays, SettingValue = "30" },
            new() { SettingKey = SystemSettingKeys.DefaultProjectTitle, SettingValue = "Test Project" },
            new() { SettingKey = LoggingSettingKeys.ClientFileLevel, SettingValue = "Debug" },
        };

        var dto = SqlSystemSettingsService.MapToSystemDto(rows);

        Assert.Equal(@"\\server\logs", dto.Logging.CentralLogPath);
        Assert.Equal(30, dto.Logging.LocalRetentionDays);
        Assert.Equal("Test Project", dto.EmailOffice.DefaultProjectTitle);
        Assert.Equal(LogLevelDto.Debug, dto.Logging.Client.FileLevel);
    }

    private static void RegisterSqlSettingsDependencies(IServiceCollection services)
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
