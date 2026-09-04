namespace SiNet.Application.Identity;

/// <summary>
/// Read-only profile of the authenticated current user for display and context.
/// Loaded through <see cref="ICurrentUserProfileService"/> — not through
/// <see cref="ICurrentUserContext"/> (which carries only <c>UserId</c>).
/// <para>
/// <see cref="Email"/> is the canonical external human identity from <c>SIUser.Email</c>
/// (see <c>docs/IDENTITY_SIUSER_GATE.md</c>). Windows/runtime login only locates the row.
/// </para>
/// </summary>
public sealed record CurrentUserProfileDto(
    int UserId,
    string DisplayName,
    string? LoginName,
    AppRole Role,
    bool IsActive,
    int? MasterPlanEmployeeId = null,
    string? Email = null,
    AppAccUserType AccUserType = AppAccUserType.NoAccUser)
{
    /// <summary>Active SIUser with <see cref="AppRole.Unauthorized"/> — pending administrator approval.</summary>
    public bool IsPendingApproval => IsActive && Role == AppRole.Unauthorized;

    /// <summary>Active SIUser with business role (≥ Employee).</summary>
    public bool HasBusinessAccess => IsActive && Role >= AppRole.Employee;
}
