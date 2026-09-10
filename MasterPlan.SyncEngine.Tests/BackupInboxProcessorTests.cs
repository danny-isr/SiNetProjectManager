using MasterPlan.SyncEngine;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class BackupInboxProcessorTests
{
    [Fact]
    public void Partial_files_are_never_claimable()
    {
        var root = Path.Combine(Path.GetTempPath(), "inbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            BackupInboxProcessor.EnsureLayout(root);
            var incoming = Path.Combine(root, BackupInboxProcessor.IncomingFolderName);
            File.WriteAllBytes(Path.Combine(incoming, "ready.bak"), [1]);
            File.WriteAllBytes(Path.Combine(incoming, "ready.bak.partial"), [2]);
            var claimable = BackupInboxProcessor.ListClaimableBaks(incoming);
            Assert.Single(claimable);
            Assert.EndsWith("ready.bak", claimable[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Server_path_keeps_processing_subfolder_and_never_uses_n_drive()
    {
        var options = new MonthlyBackupStagingOptions
        {
            ClientStagingPath = @"D:\SharedFolder\ProjectsData\MasterPlanBakup",
            ServerStagingPath = @"D:\SharedFolder\ProjectsData\MasterPlanBakup",
            MaxRetainedBackups = 5
        };
        var client = Path.Combine(options.ClientStagingPath, "Processing", "x.bak");
        var server = BackupInboxProcessor.ToServerRestorePath(client, options);
        Assert.Contains("Processing", server, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("N:\\", server, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("N:\\", BackupInboxProcessor.IncomingFolderName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inbox_mode_never_allows_older_backup()
    {
        var program = File.ReadAllText(FindRepoFile("MasterPlan.SyncEngine/Program.cs"));
        var processor = File.ReadAllText(FindRepoFile("MasterPlan.SyncEngine/BackupInboxProcessor.cs"));
        Assert.Contains("--process-backup-inbox", program, StringComparison.Ordinal);
        Assert.Contains("allowOlderOrEqualBackup: false", processor, StringComparison.Ordinal);
        Assert.DoesNotContain("allowOlderOrEqualBackup: true", processor, StringComparison.Ordinal);
        Assert.Contains("IsPartialFile", processor, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
