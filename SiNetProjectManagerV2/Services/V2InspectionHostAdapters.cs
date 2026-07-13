using SiNet.Application.Abstractions.Inspection;

namespace SiNetProjectManagerV2.Services;

/// <summary>V2 host adapters for Inspection file picker / email / screenshot / export seams.</summary>
internal sealed class V2InspectionFileTreePickerHost : IInspectionFileTreePickerHost
{
    public Task<InspectionFilePickResult?> PickReviewedPlanAsync(
        int projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<InspectionFilePickResult?>(null);

    public Task<InspectionFilePickResult?> PickNoteLinkedFileAsync(
        int projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<InspectionFilePickResult?>(null);
}

internal sealed class V2InspectionReportEmailHost : IInspectionReportEmailHost
{
    public Task<bool> SendReportEmailAsync(int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed class V2InspectionNoteScreenshotHost : IInspectionNoteScreenshotHost
{
    public Task<InspectionScreenshotUploadResult> UploadFromClipboardAsync(
        long noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionScreenshotUploadResult.Fail("העלאת צילום מסך תחובר בסלייס הבא."));
}

internal sealed class V2InspectionReportExportPort : IInspectionReportExportPort
{
    public Task<InspectionExportResult> ExportAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionExportResult.NotAvailable());

    public Task<InspectionExportResult> ShareAsync(
        int reportId, CancellationToken cancellationToken = default) =>
        Task.FromResult(InspectionExportResult.NotAvailable());

    public Task OpenTemplateAsync(int seriesId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
