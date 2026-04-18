using System.Windows.Controls;

namespace SiNetProjectManagerV2.Controls;

/// <summary>
/// Email list control — header, filter legend, grouped email ListBox, loading overlays.
/// DataContext: EmailManagementViewModel (inherited from parent).
/// Pure view extraction — no separate ViewModel needed.
/// </summary>
public partial class EmailListControl : UserControl
{
    public EmailListControl()
    {
        InitializeComponent();
    }
}
