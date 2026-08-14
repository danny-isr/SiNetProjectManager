using System.IO;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Email;
using SiNet.Infrastructure.Sql.Services.Email;
using SiNetSQL.Data;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

/// <summary>
/// Guards for LOGGING_MATERIAL_FAILURES P0b — EmailFiling / ingest failures must use IAppLogger.
/// </summary>
public sealed class EmailFilingMaterialLoggingTests
{
    [Fact]
    public void FileToProject_invalid_command_logs_Warning()
    {
        var logger = new CapturingLogger();
        var service = new SqlEmailFilingService(
            new ThrowingDbFactory(),
            new ThrowingGmailModify(),
            logger);

        var result = service.FileToProjectAsync(
                new FileEmailToProjectCommand(0, 1, "mid"),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.False(result.Succeeded);
        Assert.Contains(
            logger.Warns,
            w => w.Contains("[EmailFiling] outcome=Failed op=FileToProject", StringComparison.Ordinal)
                 && w.Contains("kind=InvalidCommand", StringComparison.Ordinal));
    }

    [Fact]
    public void Ingest_and_external_source_use_outcome_Failed_lines()
    {
        var ingest = ReadRepoFile(
            "src/SiNet.Infrastructure.Sql/Services/Email/Acc/NativeEmailAccIngestionExecutor.cs");
        Assert.Contains("[NativeAccIngest] outcome=Failed", ingest, StringComparison.Ordinal);
        Assert.Contains("LogFailed(", ingest, StringComparison.Ordinal);

        var external = ReadRepoFile(
            "src/SiNet.Infrastructure.Sql/Services/Email/Acc/NativeEmailExternalDownloadExecutor.cs");
        Assert.Contains("[NativeExternalDownload] outcome=Failed", external, StringComparison.Ordinal);
        Assert.Contains("LogFailed(", external, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : IAppLogger
    {
        public List<string> Warns { get; } = [];

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warns.Add(message);

        public void Error(string message, Exception? exception = null)
        {
        }
    }

    private sealed class ThrowingDbFactory : Microsoft.EntityFrameworkCore.IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() =>
            throw new InvalidOperationException("DB should not be used for invalid-command path.");

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("DB should not be used for invalid-command path.");
    }

    private sealed class ThrowingGmailModify : IEmailGmailModifyService
    {
        public string RootLabel => "SiNet";

        public Task<string> GetOrCreateProjectLabelAsync(string location, string projectDisplayName, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<string?> GetProjectLabelIdAsync(string location, string projectDisplayName, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<string?> GetProjectLabelIdByFullPathAsync(string fullPath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task AttachProjectLabelAsync(string gmailMessageId, string projectLabelId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task RemoveProjectLabelAsync(string gmailMessageId, string projectLabelId, bool moveToInbox = true, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<IReadOnlyList<string>> GetProjectLabelIdsOnMessageAsync(string gmailMessageId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task RemoveProjectLabelsFromMessageAsync(string gmailMessageId, IReadOnlyList<string> labelIdsToRemove, bool moveToInbox = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task ApplyTriageStatusLabelAsync(string gmailMessageId, EmailTriageStatus status, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task MarkAsReadAsync(string gmailMessageId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task RenameLabelAsync(string labelId, string newFullPath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task DeleteLabelAsync(string labelId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<IReadOnlyList<string>> ListMessageIdsByLabelAsync(string labelId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
