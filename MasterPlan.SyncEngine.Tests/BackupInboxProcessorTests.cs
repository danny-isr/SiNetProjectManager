using MasterPlan.SyncEngine;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class BackupInboxProcessorTests
{
    [Fact]
    public async Task Successful_restore_moves_file_to_Processed_and_records_intake_success()
    {
        var root = CreateTempRoot();
        var desktopCopy = Path.Combine(Path.GetTempPath(), "desktop-" + Guid.NewGuid().ToString("N") + ".bak");
        try
        {
            BackupInboxProcessor.EnsureLayout(root);
            var incoming = Path.Combine(root, BackupInboxProcessor.IncomingFolderName);
            File.WriteAllText(Path.Combine(incoming, "test.bak"), "bak-bytes");
            File.WriteAllText(desktopCopy, "desktop-original");

            var updates = new List<BackupInboxIntakeUpdate>();
            var exit = await BackupInboxProcessor.ProcessOneAsync(
                Staging(root),
                (_, _) => Task.FromResult(new MonthlyBackupResult { Success = true }),
                siDataConnectionString: null,
                new SilentLogger(),
                intakeUpdates: updates);

            Assert.Equal(0, exit);
            Assert.False(File.Exists(Path.Combine(incoming, "test.bak")));
            Assert.False(File.Exists(Path.Combine(root, BackupInboxProcessor.ProcessingFolderName, "test.bak")));
            Assert.True(File.Exists(Path.Combine(root, BackupInboxProcessor.ProcessedFolderName, "test.bak")));
            Assert.False(File.Exists(Path.Combine(root, BackupInboxProcessor.RejectedFolderName, "test.bak")));
            Assert.Equal(3, updates.Last().Status);
            Assert.Contains("שוחזר בהצלחה", updates.Last().Message, StringComparison.Ordinal);
            Assert.True(File.Exists(desktopCopy));
            Assert.Equal("desktop-original", File.ReadAllText(desktopCopy));
        }
        finally
        {
            TryDelete(root);
            if (File.Exists(desktopCopy))
                File.Delete(desktopCopy);
        }
    }

    [Fact]
    public async Task Failed_restore_moves_file_to_Rejected_and_records_intake_failure()
    {
        var root = CreateTempRoot();
        try
        {
            BackupInboxProcessor.EnsureLayout(root);
            File.WriteAllText(
                Path.Combine(root, BackupInboxProcessor.IncomingFolderName, "test.bak"),
                "bak-bytes");

            var updates = new List<BackupInboxIntakeUpdate>();
            var exit = await BackupInboxProcessor.ProcessOneAsync(
                Staging(root),
                (_, _) => throw new InvalidOperationException("SMO restore failed"),
                siDataConnectionString: null,
                new SilentLogger(),
                intakeUpdates: updates);

            Assert.Equal(1, exit);
            Assert.False(File.Exists(Path.Combine(root, BackupInboxProcessor.IncomingFolderName, "test.bak")));
            Assert.False(File.Exists(Path.Combine(root, BackupInboxProcessor.ProcessingFolderName, "test.bak")));
            Assert.True(File.Exists(Path.Combine(root, BackupInboxProcessor.RejectedFolderName, "test.bak")));
            Assert.False(File.Exists(Path.Combine(root, BackupInboxProcessor.ProcessedFolderName, "test.bak")));
            Assert.Equal(5, updates.Last().Status);
            Assert.Contains("SMO restore failed", updates.Last().Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Partial_files_are_never_claimed()
    {
        var root = CreateTempRoot();
        try
        {
            BackupInboxProcessor.EnsureLayout(root);
            var incoming = Path.Combine(root, BackupInboxProcessor.IncomingFolderName);
            File.WriteAllText(Path.Combine(incoming, "test.bak.partial"), "partial");

            var updates = new List<BackupInboxIntakeUpdate>();
            var exit = await BackupInboxProcessor.ProcessOneAsync(
                Staging(root),
                (_, _) => Task.FromResult(new MonthlyBackupResult { Success = true }),
                siDataConnectionString: null,
                new SilentLogger(),
                intakeUpdates: updates);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(incoming, "test.bak.partial")));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, BackupInboxProcessor.ProcessingFolderName)));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, BackupInboxProcessor.ProcessedFolderName)));
            Assert.Empty(updates);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Inbox_processor_never_allows_older_backup()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "MasterPlan.SyncEngine", "BackupInboxProcessor.cs"));
        Assert.Contains("allowOlderOrEqualBackup: false", source, StringComparison.Ordinal);
        Assert.Contains("allow-older=false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("allowOlderOrEqualBackup: true", source, StringComparison.Ordinal);
    }

    private static MonthlyBackupStagingOptions Staging(string root) =>
        new()
        {
            ClientStagingPath = root,
            ServerStagingPath = root,
            MaxRetainedBackups = 10
        };

    private static string CreateTempRoot() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mp-inbox-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // temp cleanup
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "MasterPlan.SyncEngine")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root from " + AppContext.BaseDirectory);
    }

    private sealed class SilentLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
