using System.Windows;
using SiNet.App.Wpf.Shell;
using SiNetProjectManagerV2.Dialogs;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host factory for the legacy <see cref="AddUserWindow"/> admin surface. Keeps the concrete
/// window type out of <see cref="SiNet.App.Wpf"/> while allowing the New System shell menu to open it.
/// </summary>
internal sealed class AddUserWindowFactory : IAddUserWindowFactory
{
    /// <inheritdoc />
    public Window Create() => new AddUserWindow();
}
