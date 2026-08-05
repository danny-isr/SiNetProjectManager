using SiNet.Application.Settings;
using SiNet.Infrastructure.Diagnostics;
using SiNet.Infrastructure.Sql.Services.Settings;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Diagnostics;

/// <summary>Defaults and persistence of the <c>Diagnostics.*</c> keys (DEV-010).</summary>
public sealed class WorkstationCrashSettingsTests
{
    [Fact]
    public void WhenNoDiagnosticsRowsExistThenTheDefaultsApply()
    {
        var dto = SqlSystemSettingsService.MapToSystemDto([]);

        Assert.Equal(string.Empty, dto.Diagnostics.CrashReportSharePath);
        Assert.Equal(SystemSettingsDefaults.DiagnosticsCrashAppFilters, dto.Diagnostics.CrashAppFilters);
        Assert.Equal(SystemSettingsDefaults.DiagnosticsCrashLookbackDays, dto.Diagnostics.CrashLookbackDays);
        Assert.Equal(SystemSettingsDefaults.DiagnosticsCrashReportRetentionDays, dto.Diagnostics.CrashReportRetentionDays);
    }

    [Fact]
    public void WhenAShareIsConfiguredThenItIsReadBack()
    {
        var rows = new List<SystemSetting>
        {
            new()
            {
                SettingKey = SystemSettingKeys.DiagnosticsCrashReportSharePath,
                SettingValue = @"\\si-win-2k19\AutoCAD Data\log\CrashReports",
            },
        };

        var dto = SqlSystemSettingsService.MapToSystemDto(rows);

        Assert.Equal(@"\\si-win-2k19\AutoCAD Data\log\CrashReports", dto.Diagnostics.CrashReportSharePath);
    }

    [Fact]
    public void WhenRetentionIsStoredAsZeroThenItIsClampedToAtLeastOneDay()
    {
        var rows = new List<SystemSetting>
        {
            new() { SettingKey = SystemSettingKeys.DiagnosticsCrashReportRetentionDays, SettingValue = "0" },
        };

        var dto = SqlSystemSettingsService.MapToSystemDto(rows);

        Assert.Equal(1, dto.Diagnostics.CrashReportRetentionDays);
    }

    [Fact]
    public void WhenOnlyTheCentralLogPathIsSetThenTheShareIsDerivedFromIt()
    {
        var rows = new List<SystemSetting>
        {
            new() { SettingKey = LoggingSettingKeys.CentralLogPath, SettingValue = @"\\server\logs" },
        };

        var resolved = FileSystemCrashReportStore.ResolveShareRoot(SqlSystemSettingsService.MapToSystemDto(rows));

        Assert.Equal(@"\\server\logs\CrashReports", resolved);
    }

    [Fact]
    public void WhenAnExplicitShareIsSetThenItWinsOverTheCentralLogPath()
    {
        var rows = new List<SystemSetting>
        {
            new() { SettingKey = LoggingSettingKeys.CentralLogPath, SettingValue = @"\\server\logs" },
            new() { SettingKey = SystemSettingKeys.DiagnosticsCrashReportSharePath, SettingValue = @"\\other\crash" },
        };

        var resolved = FileSystemCrashReportStore.ResolveShareRoot(SqlSystemSettingsService.MapToSystemDto(rows));

        Assert.Equal(@"\\other\crash", resolved);
    }

    [Fact]
    public void WhenNoPathIsConfiguredAtAllThenThereIsNoShare()
    {
        var resolved = FileSystemCrashReportStore.ResolveShareRoot(SqlSystemSettingsService.MapToSystemDto([]));

        Assert.Null(resolved);
    }

    [Fact]
    public void WhenTheDiagnosticsKeysAreDeclaredThenTheyAreManagedBySystemSettings()
    {
        Assert.Contains(SystemSettingKeys.DiagnosticsCrashReportSharePath, SystemSettingKeys.AllManaged);
        Assert.Contains(SystemSettingKeys.DiagnosticsCrashAppFilters, SystemSettingKeys.AllManaged);
        Assert.Contains(SystemSettingKeys.DiagnosticsCrashLookbackDays, SystemSettingKeys.AllManaged);
        Assert.Contains(SystemSettingKeys.DiagnosticsCrashReportRetentionDays, SystemSettingKeys.AllManaged);
    }
}
