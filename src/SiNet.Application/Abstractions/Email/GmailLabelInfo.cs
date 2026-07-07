namespace SiNet.Application.Abstractions.Email;

/// <summary>Read-only Gmail label metadata for filter dropdowns.</summary>
public sealed record GmailLabelInfo(
    string Id,
    string Name,
    string? BackgroundColor = null,
    string? TextColor = null);
