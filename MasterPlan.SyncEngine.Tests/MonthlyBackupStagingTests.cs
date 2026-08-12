using MasterPlan.SyncEngine;
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
                ServerStagingPath = @"D:\SharedFolder\ProjectsData\MasterPlanBakup",
                MaxRetainedBackups = 10
            };

            var result = MonthlyBackupStaging.PrepareForSqlRestore(source, options);

            Assert.True(result.MovedIntoStaging);
            Assert.False(File.Exists(source));
            Assert.True(File.Exists(result.ClientStagingFilePath));
            Assert.Equal(
                Path.Combine(Path.GetFullPath(@"D:\SharedFolder\ProjectsData\MasterPlanBakup"), "Db_Mp_SiEng.bak"),
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
                ServerStagingPath = @"D:\SharedFolder\ProjectsData\MasterPlanBakup",
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
    public void Default_options_use_operator_staging_paths_and_retain_ten()
    {
        var options = new MonthlyBackupStagingOptions();

        Assert.Equal(@"N:\MasterPlanBakup", options.ClientStagingPath);
        Assert.Equal(@"D:\SharedFolder\ProjectsData\MasterPlanBakup", options.ServerStagingPath);
        Assert.Equal(10, options.MaxRetainedBackups);
    }

    [Fact]
    public void ToServerRestorePath_keeps_file_name_under_server_root()
    {
        var options = new MonthlyBackupStagingOptions
        {
            ClientStagingPath = @"N:\MasterPlanBakup",
            ServerStagingPath = @"D:\SharedFolder\ProjectsData\MasterPlanBakup"
        };

        var server = MonthlyBackupStaging.ToServerRestorePath(
            @"N:\MasterPlanBakup\Db_Mp_SiEng202608020625.bak",
            options);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(@"D:\SharedFolder\ProjectsData\MasterPlanBakup"), "Db_Mp_SiEng202608020625.bak"),
            server);
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
