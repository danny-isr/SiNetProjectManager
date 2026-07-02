namespace SiNet.Application.Identity;

/// <summary>
/// User row for management dashboards (mirrors legacy <c>UserWithTaskCountDto</c> without EF types).
/// </summary>
public sealed record UserSummaryDto(
    int UserId,
    string DisplayName,
    string Email,
    string LoginName,
    bool? IsDomainGroup,
    bool IsActive,
    AppAccUserType AccUserType,
    AppRole Role,
    int OpenTaskCount,
    int? MasterPlanEmployeeId = null);
