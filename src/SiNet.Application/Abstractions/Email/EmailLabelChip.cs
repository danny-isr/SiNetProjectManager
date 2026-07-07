namespace SiNet.Application.Abstractions.Email;

/// <summary>Display chip for a Gmail label on an email list row.</summary>
public sealed record EmailLabelChip(
    string DisplayName,
    string? BackgroundColor = null,
    string? TextColor = null,
    string? BorderColor = null);
