namespace SiNetSQL.Models;

/// <summary>
/// Represents a single failed daily sync run.
/// One row is inserted per failed run — no success rows are logged.
/// Maps to dbo.Sync_RunFailures in the SiData database.
/// </summary>
public class SyncRunFailure
{
    /// <summary>
    /// Primary key (auto-increment).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Unique identifier for the sync run (generated at start of RunDailySyncAsync).
    /// </summary>
    public Guid RunId { get; set; }

    /// <summary>
    /// UTC timestamp when the sync run started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the failure was recorded.
    /// </summary>
    public DateTime FailedAt { get; set; }

    /// <summary>
    /// Machine name where the sync was running.
    /// </summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// Application version at the time of the failure.
    /// </summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>
    /// Logical stage or entity name where the error occurred
    /// (e.g., "Projects", "AcquireLock", "Initialization").
    /// </summary>
    public string ErrorStage { get; set; } = string.Empty;

    /// <summary>
    /// Full type name of the exception (e.g., "System.InvalidOperationException").
    /// </summary>
    public string ErrorType { get; set; } = string.Empty;

    /// <summary>
    /// Full exception message (Exception.ToString()).
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Stack trace of the exception (nullable — may not always be available).
    /// </summary>
    public string? StackTrace { get; set; }
}
