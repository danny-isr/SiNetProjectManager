using SiNetProjectManager.Services;
using SiNetProjectManager.ViewModels;
using SiOffice.GoogleConnector.Reports;
using SiOffice.GoogleConnector.Reports.Data;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// Interaction logic for R01ReportDialog.xaml
/// </summary>
public partial class R01ReportDialog : Window
{
    public R01ReportDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize the ViewModel with services
        if (DataContext is R01ReportViewModel vm)
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

    /// <summary>
    /// Creates the R01ReportService from configuration.
    /// Reads secrets from Windows Credential Manager vault (with fallback to appsettings.json).
    /// </summary>
    private static R01ReportService CreateReportService()
    {
        // Load non-sensitive configuration from appsettings.json
        var config = LoadConfiguration();

        // Create auth service — secrets from vault
        var clientSecretsPath = AppConfiguration.GetGoogleClientSecretsPath()
            ?? config.GoogleReports.ClientSecretsPath;
        var authService = new GoogleAuthService(
            clientSecretsPath,
            AppConfiguration.GoogleTokenStorePath,
            AppConfiguration.GoogleApplicationName);

        // Create repositories — connection strings from vault
        var replicaCs = AppConfiguration.GetConnectionString("ReplicaDatabase")
            ?? config.ConnectionStrings.ReplicaDatabase;
        var masterPlanCs = AppConfiguration.GetConnectionString("MasterPlanDatabase")
            ?? config.ConnectionStrings.MasterPlanDatabase;

        var replicaRepo = new ReplicaR01Repository(replicaCs);
        var masterPlanRepo = new MasterPlanR01Repository(masterPlanCs);

        // Create resolver
        var resolver = new DataSourceResolver(
            replicaRepo,
            masterPlanRepo,
            (decimal)config.R01.ReplicaCoverageThreshold,
            config.R01.MaxStaleDays);

        // Create report service
        return new R01ReportService(
            authService,
            resolver,
            config.GoogleReports.SharedDriveId,
            config.GoogleReports.RootReportsFolderId,
            config.GoogleReports.R01TemplateSpreadsheetId,
            config.R01.BatchSize,
            config.R01.BatchDelayMs);
    }

    /// <summary>
    /// Loads configuration from appsettings.json.
    /// </summary>
    private static ReportConfiguration LoadConfiguration()
    {
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("קובץ תצורה appsettings.json לא נמצא.", configPath);
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<ReportConfiguration>(json, new JsonSerializerOptions
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
        
        if (string.IsNullOrEmpty(config.GoogleReports.R01TemplateSpreadsheetId))
            throw new InvalidOperationException("GoogleReports:R01TemplateSpreadsheetId חסר ב-appsettings.json");

        return config;
    }
}

#region Configuration Classes

internal class ReportConfiguration
{
    public GoogleReportsConfig GoogleReports { get; set; } = new();
    public R01Config R01 { get; set; } = new();
    public ConnectionStringsConfig ConnectionStrings { get; set; } = new();
}

internal class GoogleReportsConfig
{
    public string ClientSecretsPath { get; set; } = "credentials.json";
    public string TokenStorePath { get; set; } = "%APPDATA%\\SiNet\\GoogleTokens";
    public string SharedDriveId { get; set; } = string.Empty;
    public string RootReportsFolderId { get; set; } = string.Empty;
    public string R01TemplateSpreadsheetId { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = "SiNet Reports";
}

internal class R01Config
{
    public double ReplicaCoverageThreshold { get; set; } = 0.95;
    public int BatchSize { get; set; } = 1000;
    public int BatchDelayMs { get; set; } = 100;
    public bool StepNameMatchingEnabled { get; set; } = false;
    public int MaxStaleDays { get; set; } = 45;
}

internal class ConnectionStringsConfig
{
    public string ReplicaDatabase { get; set; } = string.Empty;
    public string MasterPlanDatabase { get; set; } = string.Empty;
}

#endregion
