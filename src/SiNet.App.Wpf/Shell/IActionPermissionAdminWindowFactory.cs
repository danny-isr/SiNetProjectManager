using System.Windows;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Creates the action-permission admin window for the New System shell menu (see
/// <c>docs/IDENTITY_AND_PERMISSIONS.md</c> P6). The host supplies a legacy or migrated implementation;
/// <see cref="SiNet.App.Wpf"/> references only this port — not <c>ActionPermissionWindow</c> directly.
/// </summary>
public interface IActionPermissionAdminWindowFactory
{
    /// <summary>Creates a new action-permission management window (host-owned surface).</summary>
    Window Create();
}
