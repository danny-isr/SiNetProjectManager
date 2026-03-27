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
