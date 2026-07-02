namespace SiNet.Application.Identity;

/// <summary>Lightweight AD user row for native add-user lookup (selection fills the form only).</summary>
public sealed record DirectoryUserDto(
    string LoginName,
    string DisplayName,
    string? Email);
