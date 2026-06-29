using System.Windows.Controls;

namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// View for the rebuilt Inspection screen foundation. Hosts the five sub-area placeholders in a
/// tab layout. DataContext is the DI-resolved <see cref="InspectionShellViewModel"/>. Not yet
/// wired into navigation; the legacy Inspection window remains the active surface.
/// </summary>
public partial class InspectionShellView : UserControl
{
    public InspectionShellView(InspectionShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
