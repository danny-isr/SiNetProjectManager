namespace SiNetSQL.Models;

/// <summary>
/// Autodesk Construction Cloud (ACC) user type for SI users.
/// Determines the user's access level and permissions in ACC projects.
/// Default is NoAccUser (0) - if not explicitly set, user has no ACC access.
/// </summary>
public enum AccUserType
{
    /// <summary>
    /// User does not have ACC access.
    /// This is the default value for all users.
    /// </summary>
    NoAccUser = 0,

    /// <summary>
    /// Engineer-level ACC user with standard project access.
    /// </summary>
    Engineer = 1,

    /// <summary>
    /// Admin-level ACC user with full project management permissions.
    /// </summary>
    Admin = 2
}
