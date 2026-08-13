namespace SiNet.Application.Email;

/// <summary>One mailbox user label mapped to a SiNet project for the DEV-026 audit table.</summary>
public sealed record GmailMailboxLabelAuditRow(
    string LabelId,
    string LabelName,
    int? ParsedProjectNumber,
    string? ProjectDisplayName,
    string? PlaceName,
    string Note,
    bool IsDuplicate);
