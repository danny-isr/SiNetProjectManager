using System.ComponentModel;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.MVVM;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// Project Type Rules configuration window.
/// Allows administrators to configure which TaskTypes and Statuses are allowed per ProjectType.
/// </summary>
public partial class ProjectTypeRulesWindow : Window
{
    public ProjectTypeRulesWindow()
    {
        InitializeComponent();

        // Resolve ViewModel from DI container with required dependencies
        var dbContextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        DataContext = new ProjectTypeRulesViewModel(dbContextFactory);
    }

    /// <summary>
    /// Prompts to save unsaved changes before closing.
    /// </summary>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is ProjectTypeRulesViewModel vm && vm.HasUnsavedChanges)
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
                // No: close without saving
            }
        }
    }
}
