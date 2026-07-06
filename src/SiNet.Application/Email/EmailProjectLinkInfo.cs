namespace SiNet.Application.Email;

/// <summary>Read-only project-link projection for an email message.</summary>
public sealed record EmailProjectLinkInfo(
    bool IsLinked,
    int? ProjectId,
    string? ProjectNumber,
    string? ProjectName,
    string? DisplayName);
