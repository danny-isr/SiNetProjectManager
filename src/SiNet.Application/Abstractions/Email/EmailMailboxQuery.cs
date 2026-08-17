namespace SiNet.Application.Abstractions.Email;

/// <summary>Read-only mailbox list query pushed to <see cref="IEmailGateway.GetMailboxPageAsync"/>.</summary>
public sealed record EmailMailboxQuery
{
    public const int DefaultPageSize = 50;

    public string? FreeText { get; init; }

    public string? Subject { get; init; }

    public string? FromOrTo { get; init; }

    public string? LabelName { get; init; }

    /// <summary>Gmail API label id — preferred for label-group paging via <c>LabelIds</c>.</summary>
    public string? LabelId { get; init; }

    public EmailMailboxScope MailboxScope { get; init; } = EmailMailboxScope.Inbox;

    /// <summary>Gmail category tab filter. Default <see cref="EmailMailboxCategory.All"/> (no category clause).</summary>
    public EmailMailboxCategory Category { get; init; } = EmailMailboxCategory.All;

    public EmailProjectLinkFilter ProjectLinkFilter { get; init; } = EmailProjectLinkFilter.All;

    public int? OptionalProjectId { get; init; }

    public string? OptionalProjectLabel { get; init; }

    public int PageSize { get; init; } = DefaultPageSize;

    public bool AttachmentsOnly { get; init; }

    public bool UnreadOnly { get; init; }
}
