namespace SiNet.Application.Identity;

/// <summary>
/// Minimal user reference for action-permission query results.
/// </summary>
/// <param name="UserId">Application user identifier.</param>
/// <param name="DisplayName">Human-readable name (<c>SIUser.Name</c> in legacy).</param>
/// <param name="LoginName">Database login name when available.</param>
public sealed record UserRefDto(
    int UserId,
    string DisplayName,
    string? LoginName);
