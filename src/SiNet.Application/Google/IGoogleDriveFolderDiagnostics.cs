namespace SiNet.Application.Google;

/// <summary>
/// Outcome of probing a configured Drive folder or Shared Drive. Used by «מצב מערכת» rows
/// (see <c>docs/SYSTEM_HEALTH.md</c> §2.4).
/// </summary>
public enum GoogleDriveFolderStatus
{
    Ok = 0,
    NotConfigured = 1,
    NotAuthenticated = 2,

    /// <summary>HTTP 403 / folder not visible to the signed-in account.</summary>
    NoAccess = 3,
    NotFound = 4,
    InvalidType = 5,
    EmptyFolder = 6,

    /// <summary>
    /// Folder is reachable but write permission could not be determined (capabilities omitted).
    /// Not used when <c>requireWriteAccess</c> is true — that path reports <see cref="NoWriteAccess"/>.
    /// </summary>
    ReadOnlyOrUnknownWrite = 7,
    Error = 8,

    /// <summary>Readable (or Shared Drive visible) but the account cannot add children / write.</summary>
    NoWriteAccess = 9,
}

public sealed record GoogleDriveFolderDiagnosticResult(
    GoogleDriveFolderStatus Status,
    string? ConnectedEmail = null,
    string? FolderIdSnippet = null,
    string? FolderName = null,
    string? WebViewLink = null,
    string? TechnicalDetails = null);

/// <summary>
/// Drive / Shared Drive diagnostics over the shared user credential. Never opens a browser: a health
/// probe must not become an interactive sign-in.
/// </summary>
public interface IGoogleDriveFolderDiagnostics
{
    /// <param name="folderId">Configured Drive folder id; blank yields <see cref="GoogleDriveFolderStatus.NotConfigured"/>.</param>
    /// <param name="expectSpreadsheets">
    /// When true the folder must contain at least one spreadsheet (templates folder).
    /// </param>
    /// <param name="requireWriteAccess">
    /// When true, <c>capabilities.canAddChildren</c> must be true; otherwise
    /// <see cref="GoogleDriveFolderStatus.NoWriteAccess"/>.
    /// </param>
    Task<GoogleDriveFolderDiagnosticResult> DiagnoseAsync(
        string? folderId,
        bool expectSpreadsheets,
        bool requireWriteAccess = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same write probe MasterPlan R01/R02/R03 use: <c>Drives.Get</c> →
    /// <c>Capabilities.CanAddChildren</c>.
    /// </summary>
    Task<GoogleDriveFolderDiagnosticResult> DiagnoseSharedDriveWriteAsync(
        string? sharedDriveId,
        CancellationToken cancellationToken = default);
}
