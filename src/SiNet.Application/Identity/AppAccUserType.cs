namespace SiNet.Application.Identity;

/// <summary>
/// Autodesk Construction Cloud (ACC) access tier for a user (mirrors legacy <c>AccUserType</c> /
/// <c>SIUser.AccUserType</c>). Separate from application <see cref="AppRole"/>.
/// </summary>
public enum AppAccUserType
{
    NoAccUser = 0,
    Engineer = 1,
    Admin = 2,
}
