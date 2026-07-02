namespace SiNet.Application.Identity;

/// <summary>
/// Command to update an existing user. Mutating call — legacy <c>UserService</c> enforces Administrator-only
/// and self-protection rules (no self-deactivate, self-demote, or self login-name change).
/// </summary>
public sealed record UpdateUserCommand(
    int UserId,
    string? DisplayName,
    string? Email,
    string? LoginName,
    AppAccUserType AccUserType,
    AppRole Role,
    bool IsActive,
    int? MasterPlanEmployeeId = null,
    string? Notes = null);
