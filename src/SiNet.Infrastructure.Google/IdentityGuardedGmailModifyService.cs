using SiNet.Application.Abstractions.Email;
using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Fail-closed decorator: Gmail label/modify writes require authorized SIUser + matching Google identity
/// before any Gmail API call.
/// </summary>
public sealed class IdentityGuardedGmailModifyService(
    GmailEmailModifyService inner,
    IIdentityOperationGuard identityGuard) : IEmailGmailModifyService
{
    private readonly GmailEmailModifyService _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IIdentityOperationGuard _identityGuard =
        identityGuard ?? throw new ArgumentNullException(nameof(identityGuard));

    public string RootLabel => _inner.RootLabel;

    public async Task<string> GetOrCreateProjectLabelAsync(
        string location,
        string projectDisplayName,
        CancellationToken cancellationToken = default)
    {
        await EnsureGmailWriteAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.GetOrCreateProjectLabelAsync(location, projectDisplayName, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<string?> GetProjectLabelIdAsync(
        string location,
        string projectDisplayName,
        CancellationToken cancellationToken = default)
        => _inner.GetProjectLabelIdAsync(location, projectDisplayName, cancellationToken);

    public Task<string?> GetProjectLabelIdByFullPathAsync(
        string fullPath,
        CancellationToken cancellationToken = default)
        => _inner.GetProjectLabelIdByFullPathAsync(fullPath, cancellationToken);

    public async Task AttachProjectLabelAsync(
        string gmailMessageId,
        string projectLabelId,
        CancellationToken cancellationToken = default)
    {
        await EnsureGmailWriteAsync(cancellationToken).ConfigureAwait(false);
        await _inner.AttachProjectLabelAsync(gmailMessageId, projectLabelId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RemoveProjectLabelAsync(
        string gmailMessageId,
        string projectLabelId,
        bool moveToInbox = true,
        CancellationToken cancellationToken = default)
    {
        await EnsureGmailWriteAsync(cancellationToken).ConfigureAwait(false);
        await _inner.RemoveProjectLabelAsync(gmailMessageId, projectLabelId, moveToInbox, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> GetProjectLabelIdsOnMessageAsync(
        string gmailMessageId,
        CancellationToken cancellationToken = default)
        => _inner.GetProjectLabelIdsOnMessageAsync(gmailMessageId, cancellationToken);

    public async Task RemoveProjectLabelsFromMessageAsync(
        string gmailMessageId,
        IReadOnlyList<string> labelIdsToRemove,
        bool moveToInbox = false,
        CancellationToken cancellationToken = default)
    {
        await EnsureGmailWriteAsync(cancellationToken).ConfigureAwait(false);
        await _inner.RemoveProjectLabelsFromMessageAsync(
                gmailMessageId, labelIdsToRemove, moveToInbox, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ApplyTriageStatusLabelAsync(
        string gmailMessageId,
        EmailTriageStatus status,
        CancellationToken cancellationToken = default)
    {
        await EnsureGmailWriteAsync(cancellationToken).ConfigureAwait(false);
        await _inner.ApplyTriageStatusLabelAsync(gmailMessageId, status, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkAsReadAsync(
        string gmailMessageId,
        CancellationToken cancellationToken = default)
    {
        await EnsureGmailWriteAsync(cancellationToken).ConfigureAwait(false);
        await _inner.MarkAsReadAsync(gmailMessageId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameLabelAsync(
        string labelId,
        string newFullPath,
        CancellationToken cancellationToken = default)
    {
        await EnsureGmailWriteAsync(cancellationToken).ConfigureAwait(false);
        await _inner.RenameLabelAsync(labelId, newFullPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteLabelAsync(
        string labelId,
        CancellationToken cancellationToken = default)
    {
        await EnsureGmailWriteAsync(cancellationToken).ConfigureAwait(false);
        await _inner.DeleteLabelAsync(labelId, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> ListMessageIdsByLabelAsync(
        string labelId,
        CancellationToken cancellationToken = default)
        => _inner.ListMessageIdsByLabelAsync(labelId, cancellationToken);

    private Task EnsureGmailWriteAsync(CancellationToken cancellationToken)
        => _identityGuard.EnsureAllowedAsync(IdentityOperationKind.GmailWrite, context: null, cancellationToken);
}
