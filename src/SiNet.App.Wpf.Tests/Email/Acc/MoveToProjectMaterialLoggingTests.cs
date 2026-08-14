using System.IO;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Email.Acc;
using SiNet.Infrastructure.Sql.Services.Email.Acc;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email.Acc;

/// <summary>
/// Guards for LOGGING_MATERIAL_FAILURES P0a — MoveToProject failures must use IAppLogger.
/// </summary>
public sealed class MoveToProjectMaterialLoggingTests
{
    [Fact]
    public void Executor_source_uses_IAppLogger_not_Trace_for_file_failures()
    {
        var source = ReadRepoFile(
            "src/SiNet.Infrastructure.Sql/Services/Email/Acc/NativeEmailMoveToProjectExecutor.cs");

        Assert.Contains("IAppLogger", source, StringComparison.Ordinal);
        Assert.Contains("_logger.Error(", source, StringComparison.Ordinal);
        Assert.Contains("[MoveToProject] outcome=Failed", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Trace.TraceError($\"[MoveToProject] Failed to file attachment",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Trace.TraceWarning($\"[MoveToProject] Failed to download attachment",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Coordinator_logs_BackendNotAvailable_as_Error()
    {
        var logger = new CapturingLogger();
        var coordinator = new EmailMoveToProjectCoordinator(executor: null, logger: logger);

        var result = coordinator.MoveAsync(
            new EmailMoveToProjectCommand(InboxMessageId: 42, ProjectId: 7),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(EmailMoveToProjectOutcome.BackendNotAvailable, result.Outcome);
        Assert.Contains(
            logger.Errors,
            e => e.Contains("[MoveToProject] outcome=Failed kind=BackendNotAvailable", StringComparison.Ordinal)
                 && e.Contains("inbox=42", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : IAppLogger
    {
        public List<string> Errors { get; } = [];

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null) => Errors.Add(message);
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
