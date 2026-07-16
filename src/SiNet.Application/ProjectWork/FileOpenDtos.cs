namespace SiNet.Application.ProjectWork;

/// <summary>
/// Identifies a single version to open. Callers can supply either a <see cref="FileId"/> +
/// <see cref="AlternativeName"/> + <see cref="VersionNumber"/> to target an exact version, or a
/// <see cref="FullPath"/> directly. When <see cref="VersionNumber"/> is <see langword="null"/> the
/// latest version of the alternative is used; when <see cref="AlternativeName"/> is
/// <see langword="null"/> the file's first alternative is used. <see cref="ForceOpenWith"/> overrides
/// both the sidecar preference and the file's storage destination.
/// </summary>
public sealed record FileOpenRequest(
    int? FileId = null,
    string? AlternativeName = null,
    int? VersionNumber = null,
    string? FullPath = null,
    string? ForceOpenWith = null);

/// <summary>How a file-open request was resolved.</summary>
public enum FileOpenOutcome
{
    /// <summary>An ACC viewer tab was activated or created for the version.</summary>
    OpenedInAcc,

    /// <summary>The file was opened with the registered desktop application.</summary>
    OpenedLocally,

    /// <summary>The request couldn't be resolved (file/version not found, no path, no ACC URL, …).</summary>
    NotFound,

    /// <summary>The provider/tree is currently unavailable.</summary>
    Unavailable,

    /// <summary>An exception occurred while opening; see <see cref="FileOpenResult.Error"/>.</summary>
    Failed,
}

/// <summary>Result of a single open attempt.</summary>
public sealed record FileOpenResult(
    FileOpenOutcome Outcome,
    string? FullPath = null,
    string? AccViewerUrl = null,
    string? Error = null)
{
    /// <summary>True when the file was opened (locally or in the ACC viewer).</summary>
    public bool Success => Outcome is FileOpenOutcome.OpenedInAcc or FileOpenOutcome.OpenedLocally;
}
