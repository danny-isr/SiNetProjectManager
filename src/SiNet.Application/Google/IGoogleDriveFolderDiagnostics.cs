namespace SiNet.Application.Google;

/// <summary>
/// Outcome of probing a configured Drive folder. Mirrors the legacy <c>DiagnosticStatus</c> set so
/// the ported «מצב מערכת» rows report the same conditions (see <c>docs/SYSTEM_HEALTH.md</c> §2.4).
/// </summary>
public enum GoogleDriveFolderStatus
{
    Ok = 0,
    NotConfigured = 1,
    NotAuthenticated = 2,
    NoAccess = 3,
    NotFound = 4,
    InvalidType = 5,
    EmptyFolder = 6,

    /// <summary>Folder is reachable, but write permission could not be determined without mutating it.</summary>
    ReadOnlyOrUnknownWrite = 7,
    Error = 8,
}

public sealed record GoogleDriveFolderDiagnosticResult(
    GoogleDriveFolderStatus Status,
    string? ConnectedEmail = null,
    string? FolderIdSnippet = null,
    string? FolderName = null,
    string? WebViewLink = null,
    string? TechnicalDetails = null);

/// <summary>
/// Read-only diagnostics for a configured Google Drive folder. Never opens a browser: a health probe
/// must not become an interactive sign-in.
/// </summary>
public interface IGoogleDriveFolderDiagnostics
{
    /// <param name="folderId">Configured Drive folder id; blank yields <see cref="GoogleDriveFolderStatus.NotConfigured"/>.</param>
    /// <param name="expectSpreadsheets">
    /// When true the folder is also required to contain at least one spreadsheet, which is how the
    /// templates folder is validated. Reports folders only need to be reachable.
    /// </param>
    Task<GoogleDriveFolderDiagnosticResult> DiagnoseAsync(
        string? folderId,
        bool expectSpreadsheets,
        CancellationToken cancellationToken = default);
}
