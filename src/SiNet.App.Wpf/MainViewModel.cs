using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;

namespace SiNet.App.Wpf;

/// <summary>
/// Root host view model. Exposes the existing Inbox slice and the new Inspection shell so the
/// shell can host them as two simple tabs. Inbox stays the default surface; this does not replace
/// MainWindow content — both views are always present and switched via the tab strip.
/// </summary>
public sealed class MainViewModel
{
    public MainViewModel(InboxViewModel inbox, InspectionShellViewModel inspection)
    {
        Inbox = inbox;
        Inspection = inspection;
    }

    public InboxViewModel Inbox { get; }

    public InspectionShellViewModel Inspection { get; }
}
