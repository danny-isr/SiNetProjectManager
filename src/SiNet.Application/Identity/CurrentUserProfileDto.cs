namespace SiNet.Application.Identity;

/// <summary>
/// Read-only profile of the authenticated current user for display and context.
/// Loaded through <see cref="ICurrentUserProfileService"/> — not through
/// <see cref="ICurrentUserContext"/> (which carries only <c>UserId</c>).
/// </summary>
public sealed record CurrentUserProfileDto(
    int UserId,
    string DisplayName,
    string? LoginName,
    AppRole Role,
    bool IsActive,
    int? MasterPlanEmployeeId = null);
