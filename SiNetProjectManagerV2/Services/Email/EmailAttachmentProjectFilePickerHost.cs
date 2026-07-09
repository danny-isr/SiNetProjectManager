using SiNet.Application.Email.Detail;
using SiNetSQL.Services.EmailIngestion;

namespace SiNetProjectManagerV2.Services.Email;

/// <summary>
/// V2 host adapter for the legacy hierarchical external-material file tree picker.
/// </summary>
internal sealed class EmailAttachmentProjectFilePickerHost(IAttachmentProjectFilePicker picker)
    : IEmailAttachmentProjectFilePickerHost
{
    private readonly IAttachmentProjectFilePicker _picker =
        picker ?? throw new ArgumentNullException(nameof(picker));

    public bool IsAvailable => true;

    public Task<int?> PickProjectFileAsync(
        int projectId,
        int? currentProjectFileId,
        CancellationToken cancellationToken = default) =>
        _picker.PickAsync(projectId, currentProjectFileId, cancellationToken);
}
