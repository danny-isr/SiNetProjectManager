using System.Windows;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Creates the add-user admin window for the New System shell menu (see
/// <c>docs/IDENTITY_AND_PERMISSIONS.md</c>). The host supplies a legacy or migrated implementation;
/// <see cref="SiNet.App.Wpf"/> references only this port — not <c>AddUserWindow</c> directly.
/// </summary>
public interface IAddUserWindowFactory
{
    /// <summary>Creates a new add-user dialog window (host-owned surface).</summary>
    Window Create();
}
