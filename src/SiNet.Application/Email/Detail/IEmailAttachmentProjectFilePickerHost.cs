namespace SiNet.Application.Email.Detail;

/// <summary>
/// Host-provided hierarchical project-file picker for attachment tagging (external material tree).
/// </summary>
public interface IEmailAttachmentProjectFilePickerHost
{
    bool IsAvailable { get; }

    Task<int?> PickProjectFileAsync(
        int projectId,
        int? currentProjectFileId,
        CancellationToken cancellationToken = default);
}
