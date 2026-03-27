using System.Windows;

namespace SiNetProjectManager;

/// <summary>
/// Help Center window for inspection template documentation.
/// Displays a comprehensive Hebrew guide on template tag syntax, validation rules,
/// auto-fields, and good/bad examples in a modern RTL layout.
/// </summary>
public partial class InspectionHelpWindow : Window
{
    public InspectionHelpWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
