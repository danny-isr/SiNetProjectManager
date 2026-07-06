namespace SiNet.Application.Abstractions.Email;

/// <summary>Read-only mailbox list query pushed to <see cref="IEmailGateway.GetMailboxPageAsync"/>.</summary>
public sealed record EmailMailboxQuery
{
    public const int DefaultPageSize = 50;

    public string? FreeText { get; init; }

    public string? Subject { get; init; }

    public string? FromOrTo { get; init; }

    public string? LabelName { get; init; }

    public EmailProjectLinkFilter ProjectLinkFilter { get; init; } = EmailProjectLinkFilter.All;

    public int? OptionalProjectId { get; init; }

    public string? OptionalProjectLabel { get; init; }

    public int PageSize { get; init; } = DefaultPageSize;
}
