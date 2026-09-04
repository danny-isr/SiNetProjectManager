using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Identity;

namespace SiNet.Application.Email.QuoteSend;

/// <summary>
/// Application orchestrator for Proposal SendQuote. WPF consumes this port only —
/// never <see cref="IEmailSender"/> directly.
/// </summary>
public sealed class QuoteSendComposeService : IQuoteSendComposeService
{
    private readonly IEmailGateway _gateway;
    private readonly IEmailSender _sender;
    private readonly IEmailInboxQueryService _inboxQuery;
    private readonly IProposalSourceEmailQuery _sourceQuery;
    private readonly IQuoteSendProofStore _proofStore;
    private readonly IConnectorAuthService _auth;
    private readonly IIdentityOperationGuard? _identityGuard;

    public QuoteSendComposeService(
        IEmailGateway gateway,
        IEmailSender sender,
        IEmailInboxQueryService inboxQuery,
        IProposalSourceEmailQuery sourceQuery,
        IQuoteSendProofStore proofStore,
        IConnectorAuthService auth,
        IIdentityOperationGuard? identityGuard = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _inboxQuery = inboxQuery ?? throw new ArgumentNullException(nameof(inboxQuery));
        _sourceQuery = sourceQuery ?? throw new ArgumentNullException(nameof(sourceQuery));
        _proofStore = proofStore ?? throw new ArgumentNullException(nameof(proofStore));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _identityGuard = identityGuard;
    }

    public Task<ProposalSourceEmailRef?> GetProposalSourceEmailAsync(
        int? workflowInstanceId,
        CancellationToken cancellationToken = default)
    {
        if (workflowInstanceId is not > 0)
            return Task.FromResult<ProposalSourceEmailRef?>(null);

        return _sourceQuery.GetByWorkflowInstanceAsync(workflowInstanceId.Value, cancellationToken);
    }

    public async Task<QuoteSendComposeDraft> CreateDraftAsync(
        int projectId,
        int? workflowInstanceId,
        int? preferredInboxMessageId,
        QuoteSendComposeMode preferredMode,
        CancellationToken cancellationToken = default)
    {
        var marker = QuoteSendTrackingMarker.Create(
            Math.Max(workflowInstanceId ?? projectId, 1));

        if (preferredMode == QuoteSendComposeMode.NewCompose)
            return QuoteReplyAllComposer.BuildNewCompose(projectId, marker);

        if (preferredInboxMessageId is > 0)
        {
            var fromPick = await CreateDraftFromInboxCoreAsync(
                    projectId, preferredInboxMessageId.Value, marker, cancellationToken)
                .ConfigureAwait(false);
            if (fromPick is not null)
                return fromPick;
        }

        var source = await GetProposalSourceEmailAsync(workflowInstanceId, cancellationToken)
            .ConfigureAwait(false);
        if (source is not null)
        {
            var fromSource = await CreateDraftFromInboxCoreAsync(
                    projectId, source.InboxMessageId, marker, cancellationToken)
                .ConfigureAwait(false);
            if (fromSource is not null)
                return fromSource;
        }

        // No linked source — return empty new-compose draft; UI offers pick vs new.
        return QuoteReplyAllComposer.BuildNewCompose(projectId, marker);
    }

    public Task<QuoteSendComposeDraft?> CreateDraftFromInboxAsync(
        int projectId,
        int inboxMessageId,
        CancellationToken cancellationToken = default)
    {
        var marker = QuoteSendTrackingMarker.Create(Math.Max(projectId, 1));
        return CreateDraftFromInboxCoreAsync(projectId, inboxMessageId, marker, cancellationToken);
    }

    private async Task<QuoteSendComposeDraft?> CreateDraftFromInboxCoreAsync(
        int projectId,
        int inboxMessageId,
        string marker,
        CancellationToken cancellationToken)
    {
        if (inboxMessageId <= 0)
            return null;

        var inbox = await _inboxQuery.GetByIdAsync(inboxMessageId, cancellationToken).ConfigureAwait(false);
        if (inbox is null || string.IsNullOrWhiteSpace(inbox.InternetMessageId))
            return null;

        var details = await ResolveGmailDetailsAsync(inbox.InternetMessageId, cancellationToken)
            .ConfigureAwait(false);
        if (details is null)
            return null;

        await EnsureConnectedEmailAsync(cancellationToken).ConfigureAwait(false);
        var draft = QuoteReplyAllComposer.BuildReplyAll(
            details,
            _auth.ConnectedAccountEmail,
            projectId,
            marker);
        return draft with { SourceInboxMessageId = inboxMessageId };
    }

    public async Task<EmailSendResult> SendAsync(
        int taskId,
        int actingUserId,
        QuoteSendComposeDraft draft,
        IReadOnlyList<EmailAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (taskId <= 0)
            return EmailSendResult.Fail("Task id is required.");
        if (actingUserId <= 0)
            return EmailSendResult.Fail("Acting user id is required.");
        if (draft.To.Count == 0)
            return EmailSendResult.Fail("At least one To recipient is required.");

        if (_identityGuard is not null)
        {
            var decision = await _identityGuard
                .EvaluateAsync(IdentityOperationKind.GmailWrite, cancellationToken)
                .ConfigureAwait(false);
            if (!decision.Allowed)
            {
                return EmailSendResult.Fail(decision.Reason ?? "Identity operation denied.");
            }
        }

        var request = new EmailSendRequest
        {
            To = draft.To.ToList(),
            Cc = draft.Cc.ToList(),
            Subject = draft.Subject ?? string.Empty,
            Body = draft.Body ?? string.Empty,
            IsHtml = false,
            ThreadId = draft.Mode == QuoteSendComposeMode.ReplyAll ? draft.ThreadId : null,
            InReplyToMessageId = draft.Mode == QuoteSendComposeMode.ReplyAll
                ? draft.InReplyToMessageId
                : null,
            Attachments = attachments ?? Array.Empty<EmailAttachment>(),
        };

        var result = await _sender.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.MessageId))
            return result;

        var primaryTo = draft.To.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        await _proofStore.SaveAsync(
                taskId,
                actingUserId,
                result.MessageId!,
                draft.ThreadId,
                draft.Marker,
                primaryTo,
                cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    public Task<QuoteSendProof?> GetProofAsync(int taskId, CancellationToken cancellationToken = default)
        => _proofStore.GetLatestAsync(taskId, cancellationToken);

    private async Task EnsureConnectedEmailAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_auth.ConnectedAccountEmail))
            return;
        try
        {
            await _auth.RefreshAccountProfileAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort; Reply-All still works without self-filter.
        }
    }

    private async Task<EmailMessageDetails?> ResolveGmailDetailsAsync(
        string internetMessageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(internetMessageId))
            return null;

        string rfc822Term;
        try
        {
            rfc822Term = EmailMailboxQueryComposer.BuildRfc822MessageIdSearchTerm(internetMessageId);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var page = await _gateway.GetMailboxPageAsync(
            new EmailMailboxQuery
            {
                FreeText = rfc822Term,
                MailboxScope = EmailMailboxScope.AllMail,
                PageSize = 1,
            },
            pageToken: null,
            cancellationToken).ConfigureAwait(false);

        var summary = page.Items.FirstOrDefault();
        if (summary is null || string.IsNullOrWhiteSpace(summary.MessageId))
            return null;

        return await _gateway.GetDetailsAsync(summary.MessageId, cancellationToken).ConfigureAwait(false);
    }
}
