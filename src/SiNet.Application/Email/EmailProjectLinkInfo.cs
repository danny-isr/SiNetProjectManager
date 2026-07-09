namespace SiNet.Application.Email;



/// <summary>Read-only project-link projection for an email message or Gmail thread.</summary>

public sealed record EmailProjectLinkInfo(

    bool IsLinked,

    int? ProjectId,

    string? ProjectNumber,

    string? ProjectName,

    string? DisplayName,

    int? InboxMessageId = null,

    string? ThreadUniqueId = null,

    string? GmailThreadId = null,

    int? ThreadProjectId = null,

    string? ThreadProjectName = null,

    bool HasThreadHistory = false);

