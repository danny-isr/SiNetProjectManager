using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>
/// Per-attachment outcome emitted by <c>NativeEmailMoveToProjectExecutor</c>. Native port
/// of the legacy <c>SiNetSQL.Domain.Actions.Handlers.MoveToProjectAttachmentHandlerOutcome</c>.
/// <para>
/// The <see cref="Kind"/> values are limited to the cases the executor actually performs:
/// <c>"Filed"</c>, <c>"FiledButMoveMetadataFailed"</c>, <c>"AlreadyFiledSameSource"</c>,
/// <c>"MissingInAcc"</c>, <c>"DownloadFailed"</c>, <c>"AlreadyMovedToProject"</c>,
/// <c>"Locked"</c>, and <c>"FilingFailed"</c>.
/// </para>
/// </summary>
public sealed record MoveToProjectAttachmentOutcome(
    int InboxAttachmentId,
    string Kind,
    string? PlacedFileName,
    string? PlacedFilePath,
    FileStorageDestination? StorageDestination)
{
    public string? FiledState { get; init; }
    public FileStorageDestination? TargetDestination { get; init; }
    public string? TargetFileName { get; init; }
    public string? TargetFilePath { get; init; }
    public string? TargetAccItemId { get; init; }
    public string? TargetAccFolderId { get; init; }
    public int? TargetProjectFileId { get; init; }
    public int? TargetProjectAlternativeId { get; init; }
    public DateTime? MovedAtUtc { get; init; }
    public bool? LockedForEditing { get; init; }
    public string? MetadataStatus { get; init; }
    public string? WarningCode { get; init; }
    public string? WarningMessage { get; init; }
}
