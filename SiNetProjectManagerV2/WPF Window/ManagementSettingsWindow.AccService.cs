using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SiNetProjectManagerV2.Services;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.WPF_Window;

public partial class ManagementSettingsWindow
{
    private const string AccServiceBaseUrlDescription =
        "כתובת URL של SiOffice.AccService עבור פעולות ACC מורשות מרחוק";

    private TextBox? _accServiceBaseUrlTextBox;
    private bool _accServiceSettingsSectionInitialized;

    static ManagementSettingsWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(ManagementSettingsWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnManagementSettingsWindowLoaded));

        EventManager.RegisterClassHandler(
            typeof(Button),
            ButtonBase.ClickEvent,
            new RoutedEventHandler(OnManagementSettingsButtonClick),
            true);
    }

    private static async void OnManagementSettingsWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ManagementSettingsWindow window)
            return;

        await window.EnsureAccServiceSettingsSectionAsync();
    }

    private static async void OnManagementSettingsButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !IsSaveButton(button))
            return;

        var window = FindAncestor<ManagementSettingsWindow>(button);
        if (window is null)
            return;

        e.Handled = true;
        if (!await window.TrySaveAccServiceSettingAsync())
            return;

        window.SaveButton_Click(button, e);
    }

    private async Task EnsureAccServiceSettingsSectionAsync()
    {
        if (_accServiceSettingsSectionInitialized)
            return;

        _accServiceSettingsSectionInitialized = true;
        AddAccServiceSettingsSection();

        if (_accServiceBaseUrlTextBox is null)
            return;

        try
        {
            var savedBaseUrl = await _settingsService.GetOrDefaultAsync(
                SystemSettingKeys.AccServiceBaseUrl,
                AppConfiguration.AccServiceBaseUrl ?? string.Empty);

            _accServiceBaseUrlTextBox.Text = savedBaseUrl;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to load AccService base URL setting");
        }
    }

    private void AddAccServiceSettingsSection()
    {
        if (AccViewerMaxTabsTextBox.Parent is not StackPanel accViewerTabsRow
            || accViewerTabsRow.Parent is not StackPanel settingsStack)
        {
            return;
        }

        var insertIndex = settingsStack.Children.IndexOf(accViewerTabsRow) + 1;
        if (insertIndex <= 0)
            return;

        settingsStack.Children.Insert(insertIndex++, new Separator
        {
            Margin = new Thickness(0, 20, 0, 10)
        });

        settingsStack.Children.Insert(insertIndex++, new TextBlock
        {
            Text = "כתובת שירות ACC פנימי",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        });

        settingsStack.Children.Insert(insertIndex++, new TextBlock
        {
            Text = "כתובת ה-URL של SiOffice.AccService על שרת המשרד. כאשר הערך מלא, פעולות ACC מורשות נשלחות לשירות המרכזי; כאשר הוא ריק, האפליקציה נשארת במצב מקומי. השינוי נכנס לתוקף בהפעלה הבאה.",
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _accServiceBaseUrlTextBox = new TextBox
        {
            Width = 350,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(5, 3, 5, 3),
            FlowDirection = FlowDirection.LeftToRight,
            ToolTip = "למשל: https://SI-WIN-2K19:8443. השאר ריק למצב מקומי."
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        row.Children.Add(new TextBlock
        {
            Text = "כתובת URL:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        row.Children.Add(_accServiceBaseUrlTextBox);

        settingsStack.Children.Insert(insertIndex, row);
    }

    private async Task<bool> TrySaveAccServiceSettingAsync()
    {
        if (_accServiceBaseUrlTextBox is null)
            return true;

        var accServiceBaseUrl = _accServiceBaseUrlTextBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(accServiceBaseUrl))
        {
            if (!Uri.TryCreate(accServiceBaseUrl, UriKind.Absolute, out var uri)
                || string.IsNullOrWhiteSpace(uri.Host)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                MessageBox.Show(
                    this,
                    "נא להזין כתובת URL תקינה לשירות ACC, למשל https://SI-WIN-2K19:8443, או להשאיר ריק למצב מקומי.",
                    "שגיאה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                _accServiceBaseUrlTextBox.Focus();
                return false;
            }

            accServiceBaseUrl = accServiceBaseUrl.TrimEnd('/');
        }

        await _settingsService.SetAsync(
            SystemSettingKeys.AccServiceBaseUrl,
            accServiceBaseUrl,
            AccServiceBaseUrlDescription);

        AppConfiguration.Reload();
        return true;
    }

    private static bool IsSaveButton(Button button)
    {
        return button.Content is string text
            && string.Equals(text, "שמור", StringComparison.Ordinal);
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
