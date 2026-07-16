namespace SiNet.Application.Abstractions.Inspection;

/// <summary>AI grammar + rephrase review for inspection notes.</summary>
public interface IInspectionNoteAiReviewer
{
    Task<InspectionNoteAiReviewResult> ReviewAsync(
        string plainText, CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

public sealed record InspectionNoteAiReviewResult(
    string OriginalText,
    string? GrammarCorrected,
    string? Rephrased,
    string? ErrorMessage)
{
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public static InspectionNoteAiReviewResult Fail(string original, string error) =>
        new(original, null, null, error);
}

/// <summary>Host picks a project file tree selection for reviewed plan / note link.</summary>
public interface IInspectionFileTreePickerHost
{
    /// <summary>
    /// Multi-select reviewed plans from the live ProjectWork tree. Returns an empty list when the
    /// user confirms with no selection; <see langword="null"/> when cancelled. Requires the native
    /// ProjectWork surface to have registered an active-file provider.
    /// </summary>
    Task<IReadOnlyList<InspectionFilePickResult>?> PickReviewedPlansAsync(
        int projectId, CancellationToken cancellationToken = default);

    Task<InspectionFilePickResult?> PickNoteLinkedFileAsync(
        int projectId, CancellationToken cancellationToken = default);
}

public sealed record InspectionFilePickResult(
    string FileName,
    string? Alternative,
    string? Version,
    string? FullPath);

/// <summary>Host sends the inspection report email workflow.</summary>
public interface IInspectionReportEmailHost
{
    Task<bool> SendReportEmailAsync(int reportId, CancellationToken cancellationToken = default);
}

/// <summary>Host uploads a clipboard/screenshot attachment for a note.</summary>
public interface IInspectionNoteScreenshotHost
{
    Task<InspectionScreenshotUploadResult> UploadFromClipboardAsync(
        long noteId, CancellationToken cancellationToken = default);

    Task<InspectionScreenshotOpenResult> OpenLastAsync(
        long noteId, CancellationToken cancellationToken = default);
}

public sealed record InspectionScreenshotUploadResult(
    bool Succeeded,
    string? ErrorMessage = null,
    string? AttachmentUrl = null)
{
    public static InspectionScreenshotUploadResult Ok(string? url = null) => new(true, AttachmentUrl: url);
    public static InspectionScreenshotUploadResult Fail(string message) => new(false, message);
}

public sealed record InspectionScreenshotOpenResult(bool Succeeded, string Message)
{
    public static InspectionScreenshotOpenResult Ok(string message) => new(true, message);
    public static InspectionScreenshotOpenResult Fail(string message) => new(false, message);
}

/// <summary>Host opens a note-linked project file (or reviewed-plan fallback).</summary>
public interface IInspectionNoteLinkedFileHost
{
    Task<InspectionLinkedFileOpenResult> OpenAsync(
        InspectionLinkedFileOpenRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record InspectionLinkedFileOpenRequest(
    long NoteId,
    string? LinkedFileName,
    string? LinkedAlternative,
    string? LinkedVersion,
    int ReportId,
    string? ReviewedVersion,
    IReadOnlyList<InspectionReviewedFileRow> ReviewedFiles);

public sealed record InspectionLinkedFileOpenResult(
    bool Succeeded,
    string Message)
{
    public static InspectionLinkedFileOpenResult Ok(string message) => new(true, message);
    public static InspectionLinkedFileOpenResult Fail(string message) => new(false, message);
}
