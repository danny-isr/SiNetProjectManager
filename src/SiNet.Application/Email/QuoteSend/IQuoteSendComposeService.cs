using SiNet.Application.Abstractions.Email;

namespace SiNet.Application.Email.QuoteSend;

/// <summary>
/// Narrow G-Policy exception for Proposal <c>SendQuoteToClient</c>: resolve source email,
/// build Reply-All / new compose drafts, send via <see cref="IEmailSender"/> after an explicit
/// user action, and persist the sent MessageId as proof.
/// </summary>
public interface IQuoteSendComposeService
{
    Task<ProposalSourceEmailRef?> GetProposalSourceEmailAsync(
        int? workflowInstanceId,
        CancellationToken cancellationToken = default);

    Task<QuoteSendComposeDraft> CreateDraftAsync(
        int projectId,
        int? workflowInstanceId,
        int? preferredInboxMessageId,
        QuoteSendComposeMode preferredMode,
        CancellationToken cancellationToken = default);

    Task<QuoteSendComposeDraft?> CreateDraftFromInboxAsync(
        int projectId,
        int inboxMessageId,
        CancellationToken cancellationToken = default);

    Task<EmailSendResult> SendAsync(
        int taskId,
        int actingUserId,
        QuoteSendComposeDraft draft,
        IReadOnlyList<EmailAttachment>? attachments = null,
        CancellationToken cancellationToken = default);

    Task<QuoteSendProof?> GetProofAsync(int taskId, CancellationToken cancellationToken = default);
}

/// <summary>Loads the Proposal trigger inbox row (SQL).</summary>
public interface IProposalSourceEmailQuery
{
    Task<ProposalSourceEmailRef?> GetByWorkflowInstanceAsync(
        int workflowInstanceId,
        CancellationToken cancellationToken = default);
}

/// <summary>Persists / loads SendQuote proof on the task timeline.</summary>
public interface IQuoteSendProofStore
{
    Task SaveAsync(
        int taskId,
        int actingUserId,
        string gmailMessageId,
        string? gmailThreadId,
        string marker,
        string? primaryTo = null,
        CancellationToken cancellationToken = default);

    Task<QuoteSendProof?> GetLatestAsync(int taskId, CancellationToken cancellationToken = default);

    /// <summary>Latest proof for any SendQuoteToClient task on the project (for FollowQuote open).</summary>
    Task<QuoteSendProof?> GetLatestForProjectAsync(int projectId, CancellationToken cancellationToken = default);
}

/// <summary>Resolves SendQuote anchor metadata for opening FollowQuoteApproval Email-first.</summary>
public interface IFollowQuoteAnchorResolver
{
    Task<FollowQuoteOpenAnchor?> ResolveAsync(int followQuoteTaskId, CancellationToken cancellationToken = default);
}
