using System.Windows;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Creates the user-management admin window for the New System shell menu (see
/// <c>docs/IDENTITY_AND_PERMISSIONS.md</c>). The host supplies a legacy or migrated implementation;
/// <see cref="SiNet.App.Wpf"/> references only this port — not <c>UserManagementWindow</c> directly.
/// </summary>
public interface IUserManagementWindowFactory
{
    /// <summary>Creates a new user-management dashboard window (host-owned surface).</summary>
    Window Create();
}
