using SiNet.Application.Abstractions.Inspection;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>Default no-op hosts so the New System shell composes without V2 Google pickers.</summary>
internal sealed class NoOpInspectionFileTreePickerHost : IInspectionFileTreePickerHost
{
    public Task<IReadOnlyList<InspectionFilePickResult>?> PickReviewedPlansAsync(
        int projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InspectionFilePickResult>?>(null);

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
        Task.FromResult(InspectionScreenshotUploadResult.Fail("Screenshot upload requires the V2 host."));

    public Task<InspectionScreenshotOpenResult> OpenLastAsync(
        long noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionScreenshotOpenResult.Fail("Screenshot open requires the V2 host."));
}

internal sealed class NoOpInspectionNoteLinkedFileHost : IInspectionNoteLinkedFileHost
{
    public Task<InspectionLinkedFileOpenResult> OpenAsync(
        InspectionLinkedFileOpenRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionLinkedFileOpenResult.Fail("פתיחת קובץ מקושר דורשת Host (סביבת עבודה פתוחה)."));
}

internal sealed class EmptyInspectionTemplateCatalog : IInspectionTemplateCatalog
{
    public Task<IReadOnlyList<InspectionTemplateCatalogItem>> ListTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InspectionTemplateCatalogItem>>([]);
}
