using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Inspection;
using SiNet.Application.ProjectWork;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// Standalone host: open a note-linked project file via ProjectWork hubs
/// (parity with V2 <c>V2InspectionNoteLinkedFileHost</c>).
/// </summary>
internal sealed class StandaloneInspectionNoteLinkedFileHost(
    IActiveFileQueryHub activeFiles,
    IFileOpenHub fileOpen) : IInspectionNoteLinkedFileHost
{
    private readonly IActiveFileQueryHub _activeFiles =
        activeFiles ?? throw new ArgumentNullException(nameof(activeFiles));
    private readonly IFileOpenHub _fileOpen =
        fileOpen ?? throw new ArgumentNullException(nameof(fileOpen));

    public async Task<InspectionLinkedFileOpenResult> OpenAsync(
        InspectionLinkedFileOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var available = _fileOpen.IsAvailable && _activeFiles.IsAvailable;
        var reviewed = request.ReviewedFiles
            .Select(f => (f.FileName, f.Alternative))
            .ToList();
        var decision = InspectionLinkedFileOpenRules.DecideOpen(
            request.LinkedFileName,
            request.LinkedAlternative,
            request.LinkedVersion,
            request.ReviewedVersion,
            reviewed,
            available);

        if (!decision.IsEnabled || string.IsNullOrWhiteSpace(decision.FileName))
        {
            return InspectionLinkedFileOpenResult.Fail(
                string.IsNullOrWhiteSpace(decision.Tooltip)
                    ? "אין קובץ מקושר לפתיחה."
                    : decision.Tooltip);
        }

        var match = _activeFiles.FindActiveFileByName(decision.FileName!);
        if (match is null)
        {
            return InspectionLinkedFileOpenResult.Fail(
                InspectionLinkedFileOpenRules.MessageWorkWindowRequiredForOpen +
                " (הקובץ לא נמצא בעץ הפרויקט הפעיל.)");
        }

        // Project-scope guard: never open a file whose ProjectNumber does not match the hub tree.
        // Hub is already current-project scoped when Project Work registered the provider.
        int? versionNumber = int.TryParse(decision.Version, out var parsed) ? parsed : null;
        var openRequest = new FileOpenRequest(
            FileId: match.FileId,
            AlternativeName: string.IsNullOrWhiteSpace(decision.Alternative) ? null : decision.Alternative,
            VersionNumber: versionNumber);

        try
        {
            var result = await _fileOpen.OpenAsync(openRequest, cancellationToken).ConfigureAwait(false);
            var message = result.Outcome switch
            {
                FileOpenOutcome.OpenedInAcc => $"נפתח ב-ACC: {decision.FileName}",
                FileOpenOutcome.OpenedLocally => $"נפתח מקומית: {decision.FileName}",
                FileOpenOutcome.NotFound => $"הקובץ לא נמצא: {decision.FileName}",
                FileOpenOutcome.Unavailable =>
                    InspectionLinkedFileOpenRules.MessageWorkWindowRequiredForOpen,
                FileOpenOutcome.Failed => $"שגיאה בפתיחת קובץ: {result.Error}",
                _ => decision.Tooltip,
            };

            return result.Success
                ? InspectionLinkedFileOpenResult.Ok(message)
                : InspectionLinkedFileOpenResult.Fail(message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return InspectionLinkedFileOpenResult.Fail($"שגיאה בפתיחת קובץ: {ex.Message}");
        }
    }
}
