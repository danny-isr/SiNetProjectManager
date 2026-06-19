using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SiNetProjectManagerV2.Services;
using SiNetSQL.Services;
using SiNetSQL.Services.Health;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.Services.Health;

public static class GoogleHealthStatusMapper
{
    public static ServiceHealthState Map(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.OK => ServiceHealthState.Online,
        DiagnosticStatus.AccessibleReadOnlyOrUnknownWritePermission => ServiceHealthState.Warning,
        DiagnosticStatus.NotConfigured => ServiceHealthState.Warning,
        DiagnosticStatus.GoogleNotConfigured => ServiceHealthState.Warning,
        DiagnosticStatus.NotAuthenticated => ServiceHealthState.RequiresAuthorization,
        DiagnosticStatus.NoAccess => ServiceHealthState.Warning,
        DiagnosticStatus.NotFound => ServiceHealthState.Warning,
        DiagnosticStatus.InvalidType => ServiceHealthState.Offline,
        DiagnosticStatus.EmptyFolder => ServiceHealthState.Warning,
        DiagnosticStatus.Error => ServiceHealthState.Offline,
        _ => ServiceHealthState.Unknown
    };
}

public sealed class GoogleAuthConfigHealthCheck : IServiceHealthCheck
{
    public string Key => "google_config";
    public string DisplayName => "חיבור Google (הגדרות)";
    public string Category => "Google";
    public bool IsCritical => false;

    public Task<ServiceHealthStatus> CheckAsync(CancellationToken ct)
    {
        var status = new ServiceHealthStatus { Key = Key, DisplayName = DisplayName, Category = Category, IsCritical = IsCritical };
        if (File.Exists("client_secrets.json"))
        {
            status.State = ServiceHealthState.Online;
            status.Message = "קובץ הגדרות חיבור קיים";
        }
        else
        {
            status.State = ServiceHealthState.Warning;
            status.Message = "חיבור Google לא מוגדר בתחנה זו. יש לפנות למנהל מערכת.";
        }
        return Task.FromResult(status);
    }
}

public sealed class GoogleAccountHealthCheck : IServiceHealthCheck
{
    private readonly GoogleAuthService _auth;

    public GoogleAccountHealthCheck(GoogleAuthService auth)
    {
        _auth = auth;
    }

    public string Key => "google_account";
    public string DisplayName => "חשבון Google מחובר";
    public string Category => "Google";
    public bool IsCritical => false;

    public async Task<ServiceHealthStatus> CheckAsync(CancellationToken ct)
    {
        var status = new ServiceHealthStatus { Key = Key, DisplayName = DisplayName, Category = Category, IsCritical = IsCritical };
        if (_auth.IsAuthenticated)
        {
            status.State = ServiceHealthState.Online;
            var email = await _auth.GetCurrentUserEmailAsync();
            status.Message = $"מחובר ({email})";
        }
        else
        {
            status.State = ServiceHealthState.RequiresAuthorization;
            status.Message = "יש להתחבר לחשבון Google כדי להשתמש בשירותי Google באפליקציה.";
        }
        return status;
    }
}

public sealed class GoogleTemplatesFolderHealthCheck : IServiceHealthCheck
{
    private readonly SystemSettingsService _settings;
    private readonly GoogleDriveFolderDiagnosticService _diagnostic;

    public GoogleTemplatesFolderHealthCheck(SystemSettingsService settings, GoogleDriveFolderDiagnosticService diagnostic)
    {
        _settings = settings;
        _diagnostic = diagnostic;
    }

    public string Key => SystemSettingKeys.InspectionTemplatesFolderId;
    public string DisplayName => "תיקיית תבניות בדרייב";
    public string Category => "Google";
    public bool IsCritical => false;

    public async Task<ServiceHealthStatus> CheckAsync(CancellationToken ct)
    {
        var status = new ServiceHealthStatus { Key = Key, DisplayName = DisplayName, Category = Category, IsCritical = IsCritical };
        
        var folderId = await _settings.GetOrDefaultAsync(Key, "");
        var result = await _diagnostic.DiagnoseAsync(folderId, isTemplateFolder: true, silentOnly: true, ct: ct);

        status.State = GoogleHealthStatusMapper.Map(result.Status);

        status.Message = result.Status switch
        {
            DiagnosticStatus.NoAccess => "תיקיית תבניות הביקורת מוגדרת, אך לחשבון Google המחובר אין הרשאה לגשת אליה.",
            DiagnosticStatus.NotFound => "תיקיית תבניות הביקורת לא נמצאה או אינה גלויה לחשבון Google המחובר.",
            DiagnosticStatus.EmptyFolder => "תיקיית תבניות הביקורת נגישה, אך לא נמצאו בה קבצי Google Sheets.",
            DiagnosticStatus.NotConfigured => "לא הוגדרה תיקיית תבניות במערכת.",
            DiagnosticStatus.NotAuthenticated => "נדרש חיבור ל-Google.",
            DiagnosticStatus.OK => "תקין",
            _ => "שגיאה בגישה לתיקייה"
        };
        
        return status;
    }
}

public sealed class GoogleReportsFolderHealthCheck : IServiceHealthCheck
{
    private readonly SystemSettingsService _settings;
    private readonly GoogleDriveFolderDiagnosticService _diagnostic;

    public GoogleReportsFolderHealthCheck(SystemSettingsService settings, GoogleDriveFolderDiagnosticService diagnostic)
    {
        _settings = settings;
        _diagnostic = diagnostic;
    }

    public string Key => SystemSettingKeys.InspectionReportsFolderId;
    public string DisplayName => "תיקיית דוחות בדרייב";
    public string Category => "Google";
    public bool IsCritical => false;

    public async Task<ServiceHealthStatus> CheckAsync(CancellationToken ct)
    {
        var status = new ServiceHealthStatus { Key = Key, DisplayName = DisplayName, Category = Category, IsCritical = IsCritical };
        
        var folderId = await _settings.GetOrDefaultAsync(Key, "");
        var result = await _diagnostic.DiagnoseAsync(folderId, isTemplateFolder: false, silentOnly: true, ct: ct);

        status.State = GoogleHealthStatusMapper.Map(result.Status);

        status.Message = result.Status switch
        {
            DiagnosticStatus.NoAccess => "תיקיית הדוחות מוגדרת, אך לחשבון Google המחובר אין הרשאה לגשת אליה.",
            DiagnosticStatus.NotFound => "תיקיית הדוחות לא נמצאה או אינה גלויה לחשבון Google המחובר.",
            DiagnosticStatus.AccessibleReadOnlyOrUnknownWritePermission => "תיקיית הדוחות נגישה, אך הרשאת כתיבה לא נבדקה בסבב זה.",
            DiagnosticStatus.NotConfigured => "לא הוגדרה תיקיית דוחות במערכת.",
            DiagnosticStatus.NotAuthenticated => "נדרש חיבור ל-Google.",
            DiagnosticStatus.OK => "תקין",
            _ => "שגיאה בגישה לתיקייה"
        };
        
        return status;
    }
}
