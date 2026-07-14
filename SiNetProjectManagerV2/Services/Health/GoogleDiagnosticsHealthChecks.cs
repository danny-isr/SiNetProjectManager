using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SiNetProjectManagerV2.Services;
using SiNetSQL.Services;
using SiNetSQL.Services.Health;
using SiOffice.GoogleConnector;

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

    /// <summary>Display label for the Google account used by folder diagnostics.</summary>
    public static string FormatConnectedEmail(string? connectedEmail) =>
        string.IsNullOrWhiteSpace(connectedEmail)
        || connectedEmail.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? "לא ידוע"
            : connectedEmail.Trim();
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
        var path = AppConfiguration.GetGoogleClientSecretsPath();
        
        if (string.IsNullOrWhiteSpace(path))
        {
            status.State = ServiceHealthState.Warning;
            status.Message = "חיבור Google לא מוגדר בתחנה זו. יש לפנות למנהל מערכת.";
        }
        else if (!File.Exists(path))
        {
            status.State = ServiceHealthState.Warning;
            status.Message = "חיבור Google לא מוגדר בתחנה זו (קובץ הגדרות חסר). יש לפנות למנהל מערכת.";
        }
        else
        {
            status.State = ServiceHealthState.Online;
            status.Message = "קובץ הגדרות חיבור קיים";
        }
        return Task.FromResult(status);
    }
}

public sealed class GoogleAccountHealthCheck : IServiceHealthCheck
{
    private readonly GoogleService _auth;

    public GoogleAccountHealthCheck(GoogleService auth)
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
        
        if (!_auth.IsAuthenticated)
        {
            var credentialsPath = AppConfiguration.GetGoogleClientSecretsPath() ?? "client_secrets.json";
            var restored = await _auth.TryRestoreSessionAsync(credentialsPath, ct).ConfigureAwait(false);
            AppLogger.Info($"[Health][google_account] silent restore attempted -> {restored}");
        }

        AppLogger.Info($"[Health][google_account] IsAuthenticated = {_auth.IsAuthenticated}");

        if (_auth.IsAuthenticated)
        {
            status.State = ServiceHealthState.Online;
            var email = await _auth.GetCurrentUserEmailAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(email) || email.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                email = "לא ידוע";
            status.Message = $"מחובר ({email})";
        }
        else
        {
            status.State = ServiceHealthState.RequiresAuthorization;
            status.Message = "יש להתחבר לחשבון Google כדי להשתמש בשירותי Google באפליקציה.";
        }
        
        AppLogger.Info($"[Health][google_account] returning state = {status.State}");
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
        var email = GoogleHealthStatusMapper.FormatConnectedEmail(result.ConnectedEmail);

        AppLogger.Info($"[Health][InspectionTemplatesFolderId] DiagnosticStatus = {result.Status}, ConnectedEmail = {email}");

        status.State = GoogleHealthStatusMapper.Map(result.Status);

        status.Message = result.Status switch
        {
            DiagnosticStatus.NoAccess =>
                $"תיקיית תבניות הביקורת מוגדרת, אך לחשבון Google המחובר אין הרשאה לגשת אליה. חשבון Google: {email}",
            DiagnosticStatus.NotFound =>
                $"תיקיית תבניות הביקורת לא נמצאה או אינה גלויה לחשבון Google המחובר ({email}).",
            DiagnosticStatus.EmptyFolder =>
                $"תיקיית תבניות הביקורת נגישה, אך לא נמצאו בה קבצי Google Sheets. חשבון Google: {email}",
            DiagnosticStatus.NotConfigured => "לא הוגדרה תיקיית תבניות במערכת.",
            DiagnosticStatus.NotAuthenticated =>
                email == "לא ידוע"
                    ? "נדרש חיבור ל-Google."
                    : $"נדרש חיבור ל-Google (חשבון אחרון ידוע: {email}).",
            DiagnosticStatus.OK => $"תקין ({email})",
            _ => $"שגיאה בגישה לתיקייה. חשבון Google: {email}"
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
        var email = GoogleHealthStatusMapper.FormatConnectedEmail(result.ConnectedEmail);

        AppLogger.Info($"[Health][InspectionReportsFolderId] DiagnosticStatus = {result.Status}, ConnectedEmail = {email}");

        status.State = result.Status == DiagnosticStatus.AccessibleReadOnlyOrUnknownWritePermission 
            ? ServiceHealthState.Online 
            : GoogleHealthStatusMapper.Map(result.Status);

        status.Message = result.Status switch
        {
            DiagnosticStatus.NoAccess =>
                $"תיקיית הדוחות מוגדרת, אך לחשבון Google המחובר אין הרשאה לגשת אליה. חשבון Google: {email}",
            DiagnosticStatus.NotFound =>
                $"תיקיית הדוחות לא נמצאה או אינה גלויה לחשבון Google המחובר ({email}).",
            DiagnosticStatus.AccessibleReadOnlyOrUnknownWritePermission =>
                $"תיקיית הדוחות נגישה. חשבון Google: {email}",
            DiagnosticStatus.NotConfigured => "לא הוגדרה תיקיית דוחות במערכת.",
            DiagnosticStatus.NotAuthenticated =>
                email == "לא ידוע"
                    ? "נדרש חיבור ל-Google."
                    : $"נדרש חיבור ל-Google (חשבון אחרון ידוע: {email}).",
            DiagnosticStatus.OK => $"תקין ({email})",
            _ => $"שגיאה בגישה לתיקייה. חשבון Google: {email}"
        };
        
        return status;
    }
}
