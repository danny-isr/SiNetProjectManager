using Microsoft.Win32;
using SiNetProjectManager.WPF;
using SiNetSQL.MVVM;
using SiNetSQL.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Serilog;

namespace SiNetProjectManager
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : BaseWindow, INotifyPropertyChanged
    {
        public AppSettings Settings { get; }
        public List<string> AvailableFonts { get; }

        private readonly StatusColorService? _colorService = StatusColorServiceLocator.Instance;
        private readonly int? _currentUserId = CurrentUserContext.Instance.CurrentUserId;
        private List<UserStatusColorItem> _userStatusColors = [];

        /// <summary>
        /// Display string for the log directory (shows default path hint if empty)
        /// </summary>
        public string LogDirectoryDisplay => string.IsNullOrEmpty(Settings.LogDirectory)
            ? $"(ברירת מחדל: {AppLogger.GetDefaultLogDirectory()})"
            : Settings.LogDirectory;

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            AvailableFonts = Fonts.SystemFontFamilies.Select(f => f.Source).ToList();
            Settings = currentSettings;
            Settings.PropertyChanged += Settings_PropertyChanged;
            DataContext = this;

            LoadUserStatusColors();
        }

        private void LoadUserStatusColors()
        {
            if (_colorService == null || !_currentUserId.HasValue) return;

            try
            {
                _userStatusColors = _colorService.GetUserStatusColors(_currentUserId.Value);
                UserStatusColorsItemsControl.ItemsSource = _userStatusColors;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed to load user status colors for settings");
            }
        }

        private void UserColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            // Skip initial binding when picker is created from template
            if (e.OldValue == null) return;
            if (sender is not FrameworkElement element || element.DataContext is not UserStatusColorItem item) return;
            if (_colorService == null || !_currentUserId.HasValue) return;

            var newColor = e.NewValue;
            if (newColor == null) return;

            var hex = $"#{newColor.Value.R:X2}{newColor.Value.G:X2}{newColor.Value.B:X2}";

            // If same as global default, remove override; otherwise save as personal override
            if (string.Equals(hex, item.DefaultColorHex, StringComparison.OrdinalIgnoreCase))
                _colorService.RemoveUserOverride(_currentUserId.Value, item.StatusId);
            else
                _colorService.SetUserOverride(_currentUserId.Value, item.StatusId, hex);
        }

        private void ResetStatusColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not UserStatusColorItem item) return;
            if (_colorService == null || !_currentUserId.HasValue) return;

            _colorService.RemoveUserOverride(_currentUserId.Value, item.StatusId);

            // Refresh the list to update preview + HasOverride visibility
            LoadUserStatusColors();
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Real-time UI update: Apply settings immediately as user changes them
            // Delegates to centralized App.ApplySettings() for single source of truth
            App.ApplySettings();

            // Update LogDirectoryDisplay when LogDirectory changes
            if (e.PropertyName == nameof(Settings.LogDirectory))
            {
                OnPropertyChanged(nameof(LogDirectoryDisplay));
            }
        }

        private void BrowseLogFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "בחר תיקיית לוג",
                InitialDirectory = string.IsNullOrEmpty(Settings.LogDirectory) 
                    ? AppLogger.GetDefaultLogDirectory() 
                    : Settings.LogDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                Settings.LogDirectory = dialog.FolderName;
            }
        }

        private void TestLogWrite_Click(object sender, RoutedEventArgs e)
        {
            // Temporarily configure logger to test
            var originalEnabled = AppLogger.IsEnabled;
            var originalDir = AppLogger.LogDirectory;

            try
            {
                AppLogger.LogDirectory = string.IsNullOrEmpty(Settings.LogDirectory)
                    ? AppLogger.GetDefaultLogDirectory()
                    : Settings.LogDirectory;
                AppLogger.IsEnabled = true;

                if (AppLogger.TestWriteAccess())
                {
                    AppLogger.Info("Test write from Settings window - SUCCESS");
                    MessageBox.Show("הכתיבה לתיקיית הלוג הצליחה!", "בדיקה עברה", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("הכתיבה לתיקיית הלוג נכשלה. בדקו הרשאות.", "שגיאה", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                // Restore original settings
                AppLogger.LogDirectory = originalDir;
                AppLogger.IsEnabled = originalEnabled;
            }
        }

        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            var logDir = string.IsNullOrEmpty(Settings.LogDirectory)
                ? AppLogger.GetDefaultLogDirectory()
                : Settings.LogDirectory;

            try
            {
                if (!System.IO.Directory.Exists(logDir))
                {
                    System.IO.Directory.CreateDirectory(logDir);
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"לא ניתן לפתוח את התיקייה: {ex.Message}", "שגיאה",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearOldLogs_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "האם למחוק קבצי לוג ישנים מעל 7 ימים?",
                "ניקוי לוגים",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var originalDir = AppLogger.LogDirectory;
                AppLogger.LogDirectory = string.IsNullOrEmpty(Settings.LogDirectory)
                    ? AppLogger.GetDefaultLogDirectory()
                    : Settings.LogDirectory;

                var deleted = AppLogger.ClearOldLogs(7);
                AppLogger.LogDirectory = originalDir;

                MessageBox.Show($"נמחקו {deleted} קבצי לוג ישנים.", "ניקוי הושלם",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SettingsManager.SaveSettings(Settings);

                // Apply logging settings immediately
                AppLogger.Configure(
                    Settings.LoggingEnabled,
                    string.IsNullOrEmpty(Settings.LogDirectory) ? null : Settings.LogDirectory);

                App.ApplySettings();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Operation {Operation} failed. Context={@Context}",
                    "SaveSettings(UI)", new { FilePath = "settings.json" });
                MessageBox.Show(
                    "שמירת ההגדרות נכשלה. נסו שוב או בדקו הרשאות לקובץ ההגדרות.",
                    "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

}
