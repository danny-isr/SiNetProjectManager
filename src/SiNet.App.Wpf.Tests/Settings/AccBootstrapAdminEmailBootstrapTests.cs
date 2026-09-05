using Microsoft.EntityFrameworkCore;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Sql.Services.Settings;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Settings;

public sealed class AccBootstrapAdminEmailBootstrapTests
{
    [Fact]
    public async Task Missing_row_bootstrap_inserts_siad_default()
    {
        await using var db = CreateDb();
        await SqlSystemSettingsService.EnsureAccBootstrapAdminEmailCanonicalizedAsync(db, CancellationToken.None);

        var row = await db.SystemSettings.SingleAsync(s => s.SettingKey == SystemSettingKeys.AccBootstrapAdminEmail);
        Assert.Equal(SystemSettingsDefaults.AccBootstrapAdminEmail, row.SettingValue);
        Assert.False(await db.SystemSettings.AnyAsync(s =>
            s.SettingKey == SystemSettingKeys.LegacyAccServiceExpectedAdminEmail));
    }

    [Fact]
    public async Task Existing_custom_value_is_not_overwritten()
    {
        await using var db = CreateDb();
        db.SystemSettings.Add(new SystemSetting
        {
            SettingKey = SystemSettingKeys.AccBootstrapAdminEmail,
            SettingValue = "custom-admin@example.com",
            LastUpdated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await SqlSystemSettingsService.EnsureAccBootstrapAdminEmailCanonicalizedAsync(db, CancellationToken.None);

        var row = await db.SystemSettings.SingleAsync(s => s.SettingKey == SystemSettingKeys.AccBootstrapAdminEmail);
        Assert.Equal("custom-admin@example.com", row.SettingValue);
    }

    [Fact]
    public async Task Legacy_ExpectedAdminEmail_migrates_into_AccBootstrap_when_missing()
    {
        await using var db = CreateDb();
        db.SystemSettings.Add(new SystemSetting
        {
            SettingKey = SystemSettingKeys.LegacyAccServiceExpectedAdminEmail,
            SettingValue = "legacy@si-eng.co.il",
            LastUpdated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await SqlSystemSettingsService.EnsureAccBootstrapAdminEmailCanonicalizedAsync(db, CancellationToken.None);

        var row = await db.SystemSettings.SingleAsync(s => s.SettingKey == SystemSettingKeys.AccBootstrapAdminEmail);
        Assert.Equal("legacy@si-eng.co.il", row.SettingValue);
        Assert.False(await db.SystemSettings.AnyAsync(s =>
            s.SettingKey == SystemSettingKeys.LegacyAccServiceExpectedAdminEmail));
    }

    [Fact]
    public async Task Legacy_row_removed_when_AccBootstrap_already_exists()
    {
        await using var db = CreateDb();
        db.SystemSettings.Add(new SystemSetting
        {
            SettingKey = SystemSettingKeys.AccBootstrapAdminEmail,
            SettingValue = "siad@si-eng.co.il",
            LastUpdated = DateTime.UtcNow,
        });
        db.SystemSettings.Add(new SystemSetting
        {
            SettingKey = SystemSettingKeys.LegacyAccServiceExpectedAdminEmail,
            SettingValue = "siad@si-eng.co.il",
            LastUpdated = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await SqlSystemSettingsService.EnsureAccBootstrapAdminEmailCanonicalizedAsync(db, CancellationToken.None);

        Assert.Equal(1, await db.SystemSettings.CountAsync());
        Assert.Equal(
            "siad@si-eng.co.il",
            (await db.SystemSettings.SingleAsync()).SettingValue);
    }

    [Fact]
    public void MapToSystemDto_uses_code_default_when_row_absent()
    {
        var dto = SqlSystemSettingsService.MapToSystemDto([]);
        Assert.Equal(SystemSettingsDefaults.AccBootstrapAdminEmail, dto.Acc.AccBootstrapAdminEmail);
    }

    private static SiNetSQLDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SiNetSQLDbContext(options);
    }
}
