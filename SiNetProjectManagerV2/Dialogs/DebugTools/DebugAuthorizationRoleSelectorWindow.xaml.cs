using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetProjectManagerV2.Dialogs.DebugTools
{
    /// <summary>
    /// DEBUG ONLY tool to change the current user's role and active status in the database.
    /// This bypasses production auth mechanisms strictly for testing purposes.
    /// </summary>
    public partial class DebugAuthorizationRoleSelectorWindow : Window
    {
        private readonly SiNetSQLDbContext _dbContext;
        private Siuser? _currentUser;
        private readonly string _windowsLogin;
        
        private static readonly string BackupFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiNetProjectManagerV2",
            "debug_original_role.json");

        public DebugAuthorizationRoleSelectorWindow(SiNetSQLDbContext dbContext)
        {
            InitializeComponent();
            _dbContext = dbContext;
            _windowsLogin = System.Security.Principal.WindowsIdentity.GetCurrent().Name;

            LoadCurrentUser();
            WireWarnings();
        }

        private void LoadCurrentUser()
        {
            // Case-insensitive lookup as done in CurrentUserContext
            _currentUser = _dbContext.Siusers
                .FirstOrDefault(u => u.LoginName != null && u.LoginName.ToLower() == _windowsLogin.ToLower());

            LoginNameText.Text = $"LoginName: {_windowsLogin}";

            if (_currentUser != null)
            {
                DisplayNameText.Text = $"DisplayName: {_currentUser.Name}";
                CurrentRoleText.Text = $"Current Role: {_currentUser.Role}";
                IsActiveText.Text = $"Is Active: {_currentUser.IsActive}";

                // Backup the original state if we haven't already
                BackupOriginalState();
            }
            else
            {
                DisplayNameText.Text = "DisplayName: (User not found in DB)";
                CurrentRoleText.Text = "Current Role: N/A";
                IsActiveText.Text = "Is Active: N/A";
                RbNoChange.IsChecked = true;
                DisableOptions();
            }
        }

        private void DisableOptions()
        {
            RbAdmin.IsEnabled = false;
            RbManagement.IsEnabled = false;
            RbEmployee.IsEnabled = false;
            RbUnauthorized.IsEnabled = false;
            RbInactive.IsEnabled = false;
        }

        private void WireWarnings()
        {
            RbUnauthorized.Checked += (s, e) => WarningTextUnauthorized.Visibility = Visibility.Visible;
            RbUnauthorized.Unchecked += (s, e) => WarningTextUnauthorized.Visibility = Visibility.Collapsed;
            RbInactive.Checked += (s, e) => WarningTextInactive.Visibility = Visibility.Visible;
            RbInactive.Unchecked += (s, e) => WarningTextInactive.Visibility = Visibility.Collapsed;
        }

        private void BackupOriginalState()
        {
            if (_currentUser == null) return;
            
            try
            {
                var dir = Path.GetDirectoryName(BackupFilePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // We only write the backup if it doesn't exist, so we don't overwrite the original
                // if the user runs the tool multiple times while testing.
                if (!File.Exists(BackupFilePath))
                {
                    var state = new OriginalUserState
                    {
                        LoginName = _currentUser.LoginName,
                        Role = _currentUser.Role,
                        IsActive = _currentUser.IsActive
                    };
                    File.WriteAllText(BackupFilePath, JsonSerializer.Serialize(state));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to backup original user state: {ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUser == null) return;

            try
            {
                if (File.Exists(BackupFilePath))
                {
                    var json = File.ReadAllText(BackupFilePath);
                    var state = JsonSerializer.Deserialize<OriginalUserState>(json);
                    if (state != null && state.LoginName?.ToLower() == _windowsLogin.ToLower())
                    {
                        _currentUser.Role = state.Role;
                        _currentUser.IsActive = state.IsActive;
                        _dbContext.SaveChanges();
                        
                        // Clean up backup file since we restored
                        File.Delete(BackupFilePath);

                        MessageBox.Show("Original role and status restored successfully.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadCurrentUser();
                        RbNoChange.IsChecked = true;
                    }
                    else
                    {
                        MessageBox.Show("Backup file does not match current user.", "Restore Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("No backup file found. You might be on your original role already.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restore original user state: {ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUser != null)
            {
                if (RbNoChange.IsChecked == true)
                {
                    DialogResult = true;
                    return;
                }

                if (RbAdmin.IsChecked == true)
                {
                    _currentUser.Role = AppUserRole.Administrator;
                    _currentUser.IsActive = true;
                }
                else if (RbManagement.IsChecked == true)
                {
                    _currentUser.Role = AppUserRole.Management;
                    _currentUser.IsActive = true;
                }
                else if (RbEmployee.IsChecked == true)
                {
                    _currentUser.Role = AppUserRole.Employee;
                    _currentUser.IsActive = true;
                }
                else if (RbUnauthorized.IsChecked == true)
                {
                    _currentUser.Role = AppUserRole.Unauthorized;
                    _currentUser.IsActive = true;
                }
                else if (RbInactive.IsChecked == true)
                {
                    _currentUser.Role = AppUserRole.Employee;
                    _currentUser.IsActive = false;
                }

                _dbContext.SaveChanges();
            }

            DialogResult = true;
        }

        private class OriginalUserState
        {
            public string? LoginName { get; set; }
            public AppUserRole Role { get; set; }
            public bool IsActive { get; set; }
        }
    }
}
