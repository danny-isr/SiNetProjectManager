namespace SiNet.Application.Identity;

/// <summary>
/// Command to add a new application user. Mutating call — legacy <c>UserService</c> enforces Administrator-only.
/// </summary>
public sealed record CreateUserCommand(
    string LoginName,
    string? DisplayName = null,
    string? Email = null,
    AppRole Role = AppRole.Employee,
    AppAccUserType AccUserType = AppAccUserType.NoAccUser,
    bool IsActive = true,
    bool? IsDomainGroup = null,
    int? MasterPlanEmployeeId = null,
    string? Notes = null);
