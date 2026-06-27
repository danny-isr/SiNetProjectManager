namespace SiNetSQL.Models;

/// <summary>
/// Application-level user role. Stored in SIUser.Role column.
/// Higher value = more permissions (hierarchical).
/// </summary>
public enum AppUserRole
{
    /// <summary>User has no access to the application.</summary>
    Unauthorized = 0,

    /// <summary>Regular employee — tasks, emails, files.</summary>
    Employee = 1,

    /// <summary>Management — finances, reports, employee management.</summary>
    Management = 2,

    /// <summary>System administrator — full access including system settings.</summary>
    Administrator = 3
}
