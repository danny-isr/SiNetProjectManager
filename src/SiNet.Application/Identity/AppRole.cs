namespace SiNet.Application.Identity;

/// <summary>
/// Application-level user role (mirrors legacy <c>AppUserRole</c> / <c>SIUser.Role</c> values).
/// Higher numeric value inherits lower role capabilities. See <c>docs/IDENTITY_AND_PERMISSIONS.md</c>.
/// </summary>
public enum AppRole
{
    Unauthorized = 0,
    Employee = 1,
    Management = 2,
    Administrator = 3,
}
