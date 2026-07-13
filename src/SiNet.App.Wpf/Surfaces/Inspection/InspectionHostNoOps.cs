using SiNet.Application.Abstractions.Inspection;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>Default no-op hosts so the New System shell composes without V2 Google pickers.</summary>
internal sealed class NoOpInspectionFileTreePickerHost : IInspectionFileTreePickerHost
{
    public Task<InspectionFilePickResult?> PickReviewedPlanAsync(
        int projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<InspectionFilePickResult?>(null);

    public Task<InspectionFilePickResult?> PickNoteLinkedFileAsync(
        int projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<InspectionFilePickResult?>(null);
}

internal sealed class NoOpInspectionReportEmailHost : IInspectionReportEmailHost
{
    public Task<bool> SendReportEmailAsync(int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed class NoOpInspectionNoteScreenshotHost : IInspectionNoteScreenshotHost
{
    public Task<InspectionScreenshotUploadResult> UploadFromClipboardAsync(
        long noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionScreenshotUploadResult.Fail("העלאת צילום מסך עדיין לא מחוברת."));
}
