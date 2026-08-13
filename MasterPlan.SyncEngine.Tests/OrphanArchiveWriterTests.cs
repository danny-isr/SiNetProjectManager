using System.Text.Json;
using MasterPlan.SyncEngine;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class OrphanArchiveWriterTests
{
    [Fact]
    public void BuildFileName_uses_entity_and_timestamp()
    {
        var name = OrphanArchiveWriter.BuildFileName(
            "ProjectHoursExtended",
            new DateTime(2026, 8, 13, 7, 5, 9, DateTimeKind.Utc));

        Assert.Equal("orphan-purge-ProjectHoursExtended-20260813-070509.json", name);
    }

    [Fact]
    public void WriteEventFile_flushes_json_with_entity_and_rows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "orphan-archive-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var rows = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["ID"] = 20727,
                    ["EmployeeName"] = "Test",
                    ["Duration"] = 2.5m,
                    ["ReportDate"] = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Unspecified)
                }
            };

            var path = OrphanArchiveWriter.WriteEventFile(
                dir,
                "ProjectHoursExtended",
                new DateTime(2026, 8, 13, 5, 0, 0, DateTimeKind.Utc),
                rows);

            Assert.True(File.Exists(path));
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("ProjectHoursExtended", doc.RootElement.GetProperty("entity").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("rowCount").GetInt32());
            Assert.Equal(20727, doc.RootElement.GetProperty("rows")[0].GetProperty("ID").GetInt32());
            Assert.Equal("Test", doc.RootElement.GetProperty("rows")[0].GetProperty("EmployeeName").GetString());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DeleteExpiredFiles_removes_json_older_than_retention()
    {
        var dir = Path.Combine(Path.GetTempPath(), "orphan-archive-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var oldPath = Path.Combine(dir, "orphan-purge-ProjectHours-20200101-000000.json");
            var newPath = Path.Combine(dir, "orphan-purge-ProjectHours-20260813-000000.json");
            File.WriteAllText(oldPath, "{}");
            File.WriteAllText(newPath, "{}");
            File.SetLastWriteTimeUtc(oldPath, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newPath, new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc));

            var deleted = OrphanArchiveWriter.DeleteExpiredFiles(
                dir,
                new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc),
                retentionDays: 30);

            Assert.Equal(1, deleted);
            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(newPath));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}

public sealed class OrphanPurgeOptionsTests
{
    [Fact]
    public void FromConfiguration_defaults_enabled_and_archive_under_staging()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MasterPlanMonthlyBackup:ClientStagingPath"] = @"N:\MasterPlanBakup"
            })
            .Build();

        var options = OrphanPurgeOptions.FromConfiguration(config);

        Assert.True(options.Enabled);
        Assert.True(options.PurgeRequested);
        Assert.True(options.ShouldDelete);
        Assert.Equal(
            Path.Combine(@"N:\MasterPlanBakup", OrphanPurgeOptions.ArchiveSubfolderName),
            options.ArchiveDirectory);
        Assert.Equal(30, options.ArchiveRetentionDays);
    }

    [Fact]
    public void FromConfiguration_skip_disables_delete()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var options = OrphanPurgeOptions.FromConfiguration(config, purgeRequested: false);

        Assert.True(options.Enabled);
        Assert.False(options.PurgeRequested);
        Assert.False(options.ShouldDelete);
    }

    [Fact]
    public void HoursSyncOptions_skip_orphan_purge_sets_purge_requested_false()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var hours = HoursSyncOptions.FromConfiguration(config, skipOrphanPurge: true);

        Assert.False(hours.OrphanPurge.PurgeRequested);
        Assert.False(hours.OrphanPurge.ShouldDelete);
    }
}
