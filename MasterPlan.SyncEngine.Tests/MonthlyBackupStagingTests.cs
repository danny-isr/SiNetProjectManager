using MasterPlan.SyncEngine;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class MonthlyBackupStagingTests
{
    [Fact]
    public void When_source_outside_staging_then_file_is_moved_not_copied()
    {
        var root = CreateTempRoot();
        try
        {
            var staging = Path.Combine(root, "staging");
            var inbox = Path.Combine(root, "inbox");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(inbox);

            var source = Path.Combine(inbox, "Db_Mp_SiEng.bak");
            File.WriteAllText(source, "bak-bytes");

            var options = new MonthlyBackupStagingOptions
            {
                ClientStagingPath = staging,
                ServerStagingPath = MonthlyBackupStagingOptions.DefaultProductionStagingPath,
                MaxRetainedBackups = 10
            };

            var result = MonthlyBackupStaging.PrepareForSqlRestore(source, options);

            Assert.True(result.MovedIntoStaging);
            Assert.False(File.Exists(source));
            Assert.True(File.Exists(result.ClientStagingFilePath));
            Assert.Equal(
                Path.Combine(
                    Path.GetFullPath(MonthlyBackupStagingOptions.DefaultProductionStagingPath),
                    "Db_Mp_SiEng.bak"),
                result.ServerRestorePath);
            Assert.Equal("bak-bytes", File.ReadAllText(result.ClientStagingFilePath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void When_source_already_in_staging_then_no_move()
    {
        var root = CreateTempRoot();
        try
        {
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);
            var source = Path.Combine(staging, "already.bak");
            File.WriteAllText(source, "x");

            var options = new MonthlyBackupStagingOptions
            {
                ClientStagingPath = staging,
                ServerStagingPath = MonthlyBackupStagingOptions.DefaultProductionStagingPath,
                MaxRetainedBackups = 10
            };

            var result = MonthlyBackupStaging.PrepareForSqlRestore(source, options);

            Assert.False(result.MovedIntoStaging);
            Assert.True(File.Exists(source));
            Assert.Equal(Path.GetFullPath(source), result.ClientStagingFilePath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void When_more_than_max_retained_then_oldest_are_deleted()
    {
        var root = CreateTempRoot();
        try
        {
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);

            var keep = Path.Combine(staging, "keep.bak");
            File.WriteAllText(keep, "keep");
            File.SetLastWriteTimeUtc(keep, DateTime.UtcNow);

            for (var i = 0; i < 12; i++)
            {
                var path = Path.Combine(staging, $"old{i:00}.bak");
                File.WriteAllText(path, "old");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-(i + 1)));
            }

            var deleted = MonthlyBackupStaging.PruneOlderBackups(staging, maxRetained: 10, keepFilePath: keep);

            Assert.True(deleted.Count >= 3);
            Assert.True(File.Exists(keep));
            Assert.Equal(10, Directory.GetFiles(staging, "*.bak").Length);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Default_and_missing_config_use_server_safe_staging_path_not_mapped_N()
    {
        var defaults = new MonthlyBackupStagingOptions();
        Assert.Equal(MonthlyBackupStagingOptions.DefaultProductionStagingPath, defaults.ClientStagingPath);
        Assert.Equal(MonthlyBackupStagingOptions.DefaultProductionStagingPath, defaults.ServerStagingPath);
        Assert.DoesNotContain(@"N:\", defaults.ClientStagingPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, defaults.MaxRetainedBackups);

        var fromEmpty = MonthlyBackupStagingOptions.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        Assert.Equal(MonthlyBackupStagingOptions.DefaultProductionStagingPath, fromEmpty.ClientStagingPath);
        Assert.Equal(MonthlyBackupStagingOptions.DefaultProductionStagingPath, fromEmpty.ServerStagingPath);
        Assert.DoesNotContain(@"N:\", fromEmpty.ClientStagingPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_appsettings_client_and_server_staging_are_identical_server_paths()
    {
        var repoRoot = FindRepoRoot();
        var json = File.ReadAllText(Path.Combine(repoRoot, "MasterPlan.SyncEngine", "appsettings.json"));
        Assert.Contains(
            "\"ClientStagingPath\": \"D:\\\\SharedFolder\\\\ProjectsData\\\\MasterPlanBakup\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"ServerStagingPath\": \"D:\\\\SharedFolder\\\\ProjectsData\\\\MasterPlanBakup\"",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("N:\\\\MasterPlanBakup", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void When_client_equals_server_staging_then_prepare_still_moves_and_maps()
    {
        var root = CreateTempRoot();
        try
        {
            var staging = Path.Combine(root, "MasterPlanBakup");
            var inbox = Path.Combine(root, "inbox");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(inbox);
            var source = Path.Combine(inbox, "same-host.bak");
            File.WriteAllText(source, "payload");

            var options = new MonthlyBackupStagingOptions
            {
                ClientStagingPath = staging,
                ServerStagingPath = staging,
                MaxRetainedBackups = 10
            };

            var result = MonthlyBackupStaging.PrepareForSqlRestore(source, options);

            Assert.True(result.MovedIntoStaging);
            Assert.False(File.Exists(source));
            Assert.True(File.Exists(result.ClientStagingFilePath));
            Assert.Equal(
                Path.Combine(Path.GetFullPath(staging), "same-host.bak"),
                result.ServerRestorePath);
            Assert.Equal(result.ClientStagingFilePath, result.ServerRestorePath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ToServerRestorePath_keeps_file_name_under_server_root()
    {
        var options = new MonthlyBackupStagingOptions
        {
            ClientStagingPath = MonthlyBackupStagingOptions.DefaultProductionStagingPath,
            ServerStagingPath = MonthlyBackupStagingOptions.DefaultProductionStagingPath
        };

        var server = MonthlyBackupStaging.ToServerRestorePath(
            Path.Combine(MonthlyBackupStagingOptions.DefaultProductionStagingPath, "Db_Mp_SiEng202608020625.bak"),
            options);

        Assert.Equal(
            Path.Combine(
                Path.GetFullPath(MonthlyBackupStagingOptions.DefaultProductionStagingPath),
                "Db_Mp_SiEng202608020625.bak"),
            server);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "MasterPlan.SyncEngine")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from " + AppContext.BaseDirectory);
    }

    private static string CreateTempRoot() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mp-staging-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
