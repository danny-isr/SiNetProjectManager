namespace SiNet.Application.Abstractions.Email;

/// <summary>Gmail label modify operations for project filing and triage status labels.</summary>
public interface IEmailGmailModifyService
{
    string RootLabel { get; }

    Task<string> GetOrCreateProjectLabelAsync(
        string location,
        string projectDisplayName,
        CancellationToken cancellationToken = default);

    Task<string?> GetProjectLabelIdAsync(
        string location,
        string projectDisplayName,
        CancellationToken cancellationToken = default);

    Task<string?> GetProjectLabelIdByFullPathAsync(
        string fullPath,
        CancellationToken cancellationToken = default);

    Task AttachProjectLabelAsync(
        string gmailMessageId,
        string projectLabelId,
        CancellationToken cancellationToken = default);

    Task RemoveProjectLabelAsync(
        string gmailMessageId,
        string projectLabelId,
        bool moveToInbox = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetProjectLabelIdsOnMessageAsync(
        string gmailMessageId,
        CancellationToken cancellationToken = default);

    Task RemoveProjectLabelsFromMessageAsync(
        string gmailMessageId,
        IReadOnlyList<string> labelIdsToRemove,
        bool moveToInbox = false,
        CancellationToken cancellationToken = default);

    Task ApplyTriageStatusLabelAsync(
        string gmailMessageId,
        EmailTriageStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the Gmail <c>UNREAD</c> system label from the message.</summary>
    Task MarkAsReadAsync(
        string gmailMessageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an existing user label to <paramref name="newFullPath"/> (Gmail Labels.Update).
    /// </summary>
    Task RenameLabelAsync(
        string labelId,
        string newFullPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user label (Gmail Labels.Delete). Messages keep their other labels;
    /// the label itself is removed from the mailbox.
    /// </summary>
    Task DeleteLabelAsync(
        string labelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all Gmail message ids that currently carry <paramref name="labelId"/>
    /// (paginated). Used before label delete so the journal can retain associations.
    /// </summary>
    Task<IReadOnlyList<string>> ListMessageIdsByLabelAsync(
        string labelId,
        CancellationToken cancellationToken = default);
}
