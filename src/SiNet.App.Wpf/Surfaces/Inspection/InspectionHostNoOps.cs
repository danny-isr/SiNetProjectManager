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

    public Task<InspectionScreenshotOpenResult> OpenLastAsync(
        long noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionScreenshotOpenResult.Fail("פתיחת תמונה מצורפת עדיין לא מחוברת."));
}

internal sealed class NoOpInspectionNoteLinkedFileHost : IInspectionNoteLinkedFileHost
{
    public Task<InspectionLinkedFileOpenResult> OpenAsync(
        InspectionLinkedFileOpenRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionLinkedFileOpenResult.Fail(
            "פתיחת קובץ מקושר דורשת Host (חלון עבודה / V2)."));
}

/// <summary>Empty template catalog when Google Drive is not wired.</summary>
internal sealed class EmptyInspectionTemplateCatalog : IInspectionTemplateCatalog
{
    public Task<IReadOnlyList<InspectionTemplateCatalogItem>> ListTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InspectionTemplateCatalogItem>>([]);
}
