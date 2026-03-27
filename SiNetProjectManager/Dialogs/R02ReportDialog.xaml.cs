using SiNetProjectManager.Services;
using SiNetProjectManager.ViewModels;
using SiOffice.GoogleConnector.Reports;
using SiOffice.GoogleConnector.Reports.Data;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// Interaction logic for R02ReportDialog.xaml
/// </summary>
public partial class R02ReportDialog : Window
{
    public R02ReportDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is R02ReportViewModel vm)
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

    private static R02ReportService CreateReportService()
    {
        var config = LoadConfiguration();

        // Create auth service — secrets from vault
        var clientSecretsPath = AppConfiguration.GetGoogleClientSecretsPath()
            ?? config.GoogleReports.ClientSecretsPath;
        var authService = new GoogleAuthService(
            clientSecretsPath,
            AppConfiguration.GoogleTokenStorePath,
            AppConfiguration.GoogleApplicationName);

        // Connection strings from vault
        var masterPlanCs = AppConfiguration.GetConnectionString("MasterPlanDatabase")
            ?? config.ConnectionStrings.MasterPlanDatabase;
        var replicaCs = AppConfiguration.GetConnectionString("ReplicaDatabase")
            ?? config.ConnectionStrings.ReplicaDatabase;

        var masterPlanRepo = new MasterPlanR02Repository(masterPlanCs);
        var replicaRepo = new ReplicaR02Repository(replicaCs);

        // Expand environment variables in log path
        var logFilePath = config.R02.LogFilePath;
        if (!string.IsNullOrEmpty(logFilePath))
        {
            logFilePath = Environment.ExpandEnvironmentVariables(logFilePath);
        }

        return new R02ReportService(
            authService,
            masterPlanRepo,
            replicaRepo,
            config.GoogleReports.SharedDriveId,
            config.GoogleReports.RootReportsFolderId,
            config.GoogleReports.R02TemplateSpreadsheetId,
            config.R02.BatchSize,
            config.R02.BatchDelayMs,
            config.R02.EnableLogging,
            logFilePath);
    }

    private static R02ReportConfiguration LoadConfiguration()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("קובץ תצורה appsettings.json לא נמצא.", configPath);
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<R02ReportConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (config == null)
        {
            throw new InvalidOperationException("Failed to parse appsettings.json");
        }

        // Validate required fields
        if (string.IsNullOrEmpty(config.GoogleReports.SharedDriveId))
            throw new InvalidOperationException("GoogleReports:SharedDriveId חסר ב-appsettings.json");

        // Note: R02TemplateSpreadsheetId is optional - if empty, creates new spreadsheet

        return config;
    }
}

#region Configuration Classes

internal class R02ReportConfiguration
{
    public R02GoogleReportsConfig GoogleReports { get; set; } = new();
    public R02Config R02 { get; set; } = new();
    public R02ConnectionStringsConfig ConnectionStrings { get; set; } = new();
}

internal class R02GoogleReportsConfig
{
    public string ClientSecretsPath { get; set; } = "credentials.json";
    public string TokenStorePath { get; set; } = "%APPDATA%\\SiNet\\GoogleTokens";
    public string SharedDriveId { get; set; } = string.Empty;
    public string RootReportsFolderId { get; set; } = string.Empty;
    public string R02TemplateSpreadsheetId { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "SiNet Reports";
}

internal class R02Config
{
    public int BatchSize { get; set; } = 1000;
    public int BatchDelayMs { get; set; } = 100;
    public bool EnableLogging { get; set; } = false;
    public string LogFilePath { get; set; } = "%APPDATA%\\SiNet\\Logs\\R02_Debug.log";
}

internal class R02ConnectionStringsConfig
{
    public string ReplicaDatabase { get; set; } = string.Empty;
    public string MasterPlanDatabase { get; set; } = string.Empty;
}

#endregion
