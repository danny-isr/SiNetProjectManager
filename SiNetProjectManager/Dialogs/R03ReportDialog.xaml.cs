using Microsoft.Extensions.DependencyInjection;
using SiNetProjectManager.Services;
using SiNetProjectManager.ViewModels;
using SiOffice.GoogleConnector.Reports;
using SiOffice.GoogleConnector.Reports.Data;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// Interaction logic for R03ReportDialog.xaml
/// </summary>
public partial class R03ReportDialog : Window
{
    public R03ReportDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is R03ReportViewModel vm)
        {
            try
            {
                var reportService = CreateReportService();
                vm.Initialize(reportService);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"שגיאה באתחול שירות הדוחות:\n{ex.Message}\n\nודא שקובץ appsettings.json קיים ומוגדר כראוי.",
                    "שגיאה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static R03ReportService CreateReportService()
    {
        var config = LoadConfiguration();

        // Shared auth service (singleton — single auth per session)
        var authService = App.ServiceProvider.GetRequiredService<GoogleAuthService>();

        var replicaCs = AppConfiguration.GetConnectionString("ReplicaDatabase")
            ?? config.ConnectionStrings.ReplicaDatabase;

        var repo = new ReplicaR03Repository(replicaCs);

        return new R03ReportService(
            authService,
            repo,
            config.GoogleReports.SharedDriveId,
            config.GoogleReports.RootReportsFolderId,
            config.R03.BatchSize,
            config.R03.BatchDelayMs);
    }

    private static R03ReportConfiguration LoadConfiguration()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        if (!File.Exists(configPath))
            throw new FileNotFoundException("קובץ תצורה appsettings.json לא נמצא.", configPath);

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<R03ReportConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (config == null)
            throw new InvalidOperationException("Failed to parse appsettings.json");

        if (string.IsNullOrEmpty(config.GoogleReports.SharedDriveId))
            throw new InvalidOperationException("GoogleReports:SharedDriveId חסר ב-appsettings.json");

        return config;
    }
}

#region Configuration Classes

internal class R03ReportConfiguration
{
    public R03GoogleReportsConfig GoogleReports { get; set; } = new();
    public R03Config R03 { get; set; } = new();
    public R03ConnectionStringsConfig ConnectionStrings { get; set; } = new();
}

internal class R03GoogleReportsConfig
{
    public string ClientSecretsPath { get; set; } = "credentials.json";
    public string TokenStorePath { get; set; } = "%APPDATA%\\SiNet\\GoogleTokens";
    public string SharedDriveId { get; set; } = string.Empty;
    public string RootReportsFolderId { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "SiNet Reports";
}

internal class R03Config
{
    public int BatchSize { get; set; } = 1000;
    public int BatchDelayMs { get; set; } = 100;
}

internal class R03ConnectionStringsConfig
{
    public string ReplicaDatabase { get; set; } = string.Empty;
}

#endregion
