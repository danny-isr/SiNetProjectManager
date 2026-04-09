using System.ComponentModel;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.MVVM;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// User Management Dashboard window.
/// Allows administrators to view, search, edit, and toggle IsActive for all users.
/// </summary>
public partial class UserManagementWindow : Window
{
    public UserManagementWindow()
    {
        InitializeComponent();

        var dbContextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        DataContext = new UserManagementViewModel(dbContextFactory);
    }

    /// <summary>
    /// Forces the ComboBox SelectedItem binding to push the selected value
    /// back to the source property. Workaround for a known WPF issue where
    /// ComboBox.SelectedItem inside a read-only DataGrid CellTemplate may
    /// silently fail to propagate selection changes through TwoWay binding.
    /// </summary>
    private void OnEditComboBoxSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox cb)
        {
            var binding = cb.GetBindingExpression(
                System.Windows.Controls.Primitives.Selector.SelectedItemProperty);
            binding?.UpdateSource();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is UserManagementViewModel vm && vm.HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "יש שינויים שלא נשמרו. האם לשמור אותם לפני סגירת החלון?",
                "שינויים לא נשמרו",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    vm.SaveCommand.Execute(null);
                    break;
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    break;
            }
        }
    }
}
