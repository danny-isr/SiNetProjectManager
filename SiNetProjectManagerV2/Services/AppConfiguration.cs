using Microsoft.Extensions.Configuration;
using SiNetSQL.Services;
using SiNetSQL.Services.AccBootstrap;
using System.IO;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Provides centralized access to application configuration.
/// 
/// CONFIGURATION LOADING ORDER (later overrides earlier):
/// 1. appsettings.json - Base configuration (checked into source control, no secrets)
/// 2. appsettings.local.json - Local overrides (NOT in source control)
/// 3. Environment variables - Runtime overrides
/// 4. SystemSettings DB overrides - selected admin-managed settings, when DB is reachable
/// 
/// USAGE:
/// - Vault: API keys, client secrets, connection strings (encrypted per Windows user)
/// - appsettings.json: Contains non-sensitive configuration only
/// - appsettings.local.json: Contains environment-specific overrides
/// - Environment variables: For CI/CD and containerized deployments
/// - SystemSettings: Admin-managed operational settings shared by all clients
/// </summary>
public static class AppConfiguration
{
    private const string AccServiceBaseUrlConfigurationKey = "AccService:BaseUrl";

    private static IConfiguration? _configuration;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the application configuration.
    /// Lazily loads configuration on first access.
    /// </summary>
    public static IConfiguration Configuration
    {
        get
        {
            if (_configuration == null)
            {
                lock (_lock)
                {
                    _configuration ??= BuildConfiguration();
                }
            }
            return _configuration;
        }
    }

    /// <summary>
    /// Builds the configuration from all sources.
    /// </summary>
    private static IConfiguration BuildConfiguration()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            // Base configuration (checked into source control)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            // Local overrides (NOT in source control - contains secrets/environment-specific values)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            // Environment variables (prefix: SINET_ to avoid conflicts)
            .AddEnvironmentVariables(prefix: "SINET_");

        var configuration = builder.Build();
        var databaseOverrides = LoadDatabaseConfigurationOverrides(configuration);
        if (databaseOverrides.Count > 0)
        {
            builder.AddInMemoryCollection(databaseOverrides);
            configuration = builder.Build();
        }

        return configuration;
    }

    /// <summary>
    /// Loads selected DB-backed configuration values that must be available during
    /// early DI bootstrap, before SystemSettingsService can be resolved.
    /// </summary>
    private static IReadOnlyDictionary<string, string?> LoadDatabaseConfigurationOverrides(IConfiguration baseConfiguration)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var connectionString = CredentialVaultService.GetSecret($"{SecretKeys.ConnectionStringPrefix}SiNetDatabase")
            ?? baseConfiguration.GetSection("ConnectionStrings")["SiNetDatabase"];

        if (string.IsNullOrWhiteSpace(connectionString))
            return values;

        try
        {
            var accServiceBaseUrl = ReadSystemSetting(connectionString, SystemSettingKeys.AccServiceBaseUrl);
            if (!string.IsNullOrWhiteSpace(accServiceBaseUrl))
            {
                values[AccServiceBaseUrlConfigurationKey] = accServiceBaseUrl.Trim();
            }
        }
        catch (Exception ex)
        {
            // Configuration must remain bootable even if DB-backed settings are unavailable.
            System.Diagnostics.Debug.WriteLine($"[AppConfiguration] Failed to load DB-backed settings: {ex.Message}");
        }

        return values;
    }

    private static string? ReadSystemSetting(string connectionString, string key)
    {
        using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) [SettingValue] FROM [SystemSettings] WHERE [SettingKey] = @key";
        command.Parameters.AddWithValue("@key", key);

        connection.Open();
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// Forces a reload of the configuration.
    /// Use sparingly - typically only needed for testing or hot-reload scenarios.
    /// </summary>
    public static void Reload()
    {
        lock (_lock)
        {
            _configuration = BuildConfiguration();
        }
    }

    /// <summary>
    /// Gets the Autodesk section of configuration.
    /// </summary>
    public static IConfigurationSection Autodesk => Configuration.GetSection("Autodesk");

    /// <summary>
    /// Gets the Active Directory section of configuration.
    /// </summary>
    public static IConfigurationSection ActiveDirectory => Configuration.GetSection("ActiveDirectory");

    /// <summary>
    /// Gets the configured AD domain name. Empty/null = auto-detect (domain-joined machines).
    /// </summary>
    public static string? AdDomainName
    {
        get
        {
            var value = ActiveDirectory["DomainName"];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>
    /// Gets the Google Reports section of configuration.
    /// </summary>
    public static IConfigurationSection GoogleReports => Configuration.GetSection("GoogleReports");

    /// <summary>
    /// Gets the Connection Strings section of configuration.
    /// </summary>
    public static IConfigurationSection ConnectionStrings => Configuration.GetSection("ConnectionStrings");

    /// <summary>
    /// Gets the Logging section of configuration.
    /// </summary>
    public static IConfigurationSection Logging => Configuration.GetSection("Logging");

    /// <summary>
    /// Gets the ACC service base URL after DB-backed SystemSettings overrides are applied.
    /// Empty/null = local in-process ACC provisioning mode.
    /// </summary>
    public static string? AccServiceBaseUrl
    {
        get
        {
            var value = Configuration[AccServiceBaseUrlConfigurationKey];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>
    /// Gets the UNC or local path for centralized error logging across all deployed instances.
    /// Each user gets a subfolder by Windows username under this path.
    /// Empty/null = central logging is disabled (only local logs).
    /// <para>
    /// Can be set via:
    /// <list type="bullet">
    ///   <item>appsettings.json: "Logging:CentralLogPath"</item>
    ///   <item>appsettings.local.json (override)</item>
    ///   <item>Environment variable: SINET_Logging__CentralLogPath</item>
    /// </list>
    /// </para>
    /// Example: <c>\\server\llog</c>
    /// </summary>
    public static string? CentralLogPath
    {
        get
        {
            var value = Logging["CentralLogPath"];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // VAULT-BASED SECRET ACCESSORS
    // Priority: Vault → appsettings.json fallback
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets the Gemini API key. Vault → appsettings.json fallback.
    /// </summary>
    public static string? GeminiApiKey =>
        CredentialVaultService.GetSecret(SecretKeys.GeminiApiKey)
        ?? Configuration["GeminiApiKey"];

    /// <summary>
    /// Gets a connection string by name. Vault → appsettings.json fallback.
    /// </summary>
    public static string? GetConnectionString(string name) =>
        CredentialVaultService.GetSecret($"{SecretKeys.ConnectionStringPrefix}{name}")
        ?? ConnectionStrings[name];

    /// <summary>
    /// Gets the path to the Google OAuth client secrets file.
    /// If the content is stored in the vault, writes it to a secure per-user location
    /// and returns that path. Falls back to the configured file path in appsettings.json.
    /// </summary>
    public static string? GetGoogleClientSecretsPath()
    {
        // Try vault first — content stored encrypted per-user
        var content = CredentialVaultService.GetSecret(SecretKeys.GoogleClientSecrets);
        if (!string.IsNullOrEmpty(content))
        {
            var securePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiNet", "Secure");
            Directory.CreateDirectory(securePath);

            var filePath = Path.Combine(securePath, "credentials.json");
            File.WriteAllText(filePath, content);
            return filePath;
        }

        // Fallback to config file path
        var configuredPath = GoogleReports["ClientSecretsPath"];
        if (string.IsNullOrEmpty(configuredPath))
            return null;

        var fullPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredPath);

        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>
    /// Gets the Google Reports token store path from configuration.
    /// </summary>
    public static string GoogleTokenStorePath =>
        GoogleReports["TokenStorePath"] ?? "%APPDATA%\\SiNet\\GoogleTokens";

    /// <summary>
    /// Gets the Google Reports application name from configuration.
    /// </summary>
    public static string GoogleApplicationName =>
        GoogleReports["ApplicationName"] ?? "SiNet Reports";

    /// <summary>
    /// Gets the Google Drive section of configuration (project-file storage destination).
    /// Distinct from <see cref="GoogleReports"/>, which is the Reports/Sheets export feature.
    /// Required keys (when GoogleDrive is used as a real storage destination):
    /// <list type="bullet">
    /// <item><c>GoogleDrive:SharedDriveId</c> — id of the Shared Drive holding project files.</item>
    /// <item><c>GoogleDrive:ProjectsRootFolderId</c> — folder id under which project subtrees live.</item>
    /// </list>
    /// If either key is missing, the Drive destination is treated as unavailable
    /// (operations fail explicitly; no fallback to FileServer / ACC).
    /// </summary>
    public static IConfigurationSection GoogleDrive => Configuration.GetSection("GoogleDrive");

    /// <summary>Shared Drive id used for project-file storage, or null if unset.</summary>
    public static string? GoogleDriveSharedDriveId
    {
        get
        {
            var v = GoogleDrive["SharedDriveId"];
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }
    }

    /// <summary>Drive folder id (inside the Shared Drive) under which project subtrees live, or null if unset.</summary>
    public static string? GoogleDriveProjectsRootFolderId
    {
        get
        {
            var v = GoogleDrive["ProjectsRootFolderId"];
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }
    }

    // === TEMP DEV: Autodesk Office Inbox configuration helpers ===

    /// <summary>
    /// Gets the Office Inbox project name from configuration.
    /// Default: "מיילים למשרד - POC 4"
    /// </summary>
    public static string InboxProjectName => 
        Autodesk["InboxProjectName"] ?? "מיילים למשרד - POC 4";

    /// <summary>
    /// Gets the Office Inbox folder name from configuration.
    /// Default: "_Inbox"
    /// </summary>
    public static string InboxFolderName => 
        Autodesk["InboxFolderName"] ?? "_Inbox";

    /// <summary>
    /// Gets whether to force-create the Office Inbox project if not found.
    /// Default: true (DEV behavior)
    /// </summary>
    public static bool ForceCreateOfficeInboxProject
    {
        get
        {
            var value = Autodesk["ForceCreateOfficeInboxProject"];
            if (string.IsNullOrEmpty(value))
                return true; // Default

            return bool.TryParse(value, out var result) && result;
        }
    }

    /// <summary>
    /// Gets the platform to use for creating the Office Inbox project.
    /// Default: AccNative (new ACC Admin API)
    /// </summary>
    public static CreateProjectPlatform CreateOfficeInboxPlatform
    {
        get
        {
            var value = Autodesk["CreateOfficeInboxPlatform"];
            if (string.IsNullOrEmpty(value))
                return CreateProjectPlatform.AccNative;

            return value.Equals("LegacyBim360", StringComparison.OrdinalIgnoreCase)
                ? CreateProjectPlatform.LegacyBim360
                : CreateProjectPlatform.AccNative;
        }
    }

    /// <summary>
    /// Gets the email address of the user to assign as Project Admin when creating ACC-native projects.
    /// This is REQUIRED for ACC-native projects to enable Docs.
    /// Can be set via:
    /// - appsettings.json: "Autodesk:BootstrapAdminEmail"
    /// - appsettings.local.json (recommended for real email)
    /// - Environment variable: SINET_Autodesk__BootstrapAdminEmail
    /// </summary>
    public static string BootstrapAdminEmail => 
        Autodesk["BootstrapAdminEmail"] ?? string.Empty;

    /// <summary>
    /// Gets whether to run in DryRun mode (no mutating API calls, only logging).
    /// Useful for debugging the bootstrap flow without side effects.
    /// Default: false
    /// </summary>
    public static bool DryRun
    {
        get
        {
            var value = Autodesk["DryRun"];
            if (string.IsNullOrEmpty(value))
                return false; // Default

            return bool.TryParse(value, out var result) && result;
        }
    }

    /// <summary>
    /// Gets the OAuth redirect port for the localhost callback listener.
    /// This MUST match the Callback URL configured in APS Console exactly.
    /// Default: 8080
    /// 
    /// Can be set via:
    /// - appsettings.json: "Autodesk:OAuthRedirectPort"
    /// - appsettings.local.json
    /// - Environment variable: SINET_Autodesk__OAuthRedirectPort
    /// </summary>
    public static int OAuthRedirectPort
    {
        get
        {
            var value = Autodesk["OAuthRedirectPort"];
            if (string.IsNullOrEmpty(value) || !int.TryParse(value, out var port))
                return 8080; // Default port
            return port;
        }
    }

    /// <summary>
    /// Gets the full OAuth redirect URI for the localhost callback.
    /// Constructed from OAuthRedirectPort.
    /// Example: http://localhost:8080/
    /// </summary>
    public static string OAuthRedirectUri => $"http://localhost:{OAuthRedirectPort}/";

    // === END TEMP DEV ===

    // === WebView2 Configuration ===

    /// <summary>
    /// Gets the WebView2 section of configuration.
    /// </summary>
    public static IConfigurationSection WebView2 => Configuration.GetSection("WebView2");

    /// <summary>
    /// Base path for per-user WebView2 UserDataFolders.
    /// Each Google account gets a subdirectory under this path to persist SSO sessions.
    /// Default: %LOCALAPPDATA%\SiNetProjectManagerV2\WebView2UserData
    /// 
    /// Can be set via:
    /// - appsettings.json: "WebView2:UserDataBasePath"
    /// - appsettings.local.json
    /// - Environment variable: SINET_WebView2__UserDataBasePath
    /// </summary>
    public static string WebView2UserDataBasePath =>
        WebView2["UserDataBasePath"]
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiNetProjectManagerV2",
            "WebView2UserData");

    /// <summary>
    /// Base path for email attachment downloads intercepted from WebView2.
    /// Downloads are saved to ACC-mirrored folder structure under this path.
    /// Default: %LOCALAPPDATA%\SiNetProjectManagerV2\Downloads
    /// 
    /// Can be set via:
    /// - appsettings.json: "WebView2:DownloadBasePath"
    /// - appsettings.local.json
    /// - Environment variable: SINET_WebView2__DownloadBasePath
    /// </summary>
    public static string DownloadBasePath =>
        WebView2["DownloadBasePath"]
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiNetProjectManagerV2",
            "Downloads");

    /// <summary>
    /// Maximum file size (in MB) for automatic ACC upload / ZIP extraction.
    /// Files larger than this trigger a confirmation dialog.
    /// Default: 200 MB
    /// 
    /// Can be set via:
    /// - appsettings.json: "WebView2:MaxUploadFileSizeMb"
    /// - appsettings.local.json
    /// - Environment variable: SINET_WebView2__MaxUploadFileSizeMb
    /// </summary>
    public static long MaxUploadFileSizeMb
    {
        get
        {
            var value = WebView2["MaxUploadFileSizeMb"];
            if (!string.IsNullOrEmpty(value) && long.TryParse(value, out var mb))
                return mb;
            return 200;
        }
    }

    /// <summary>
    /// Maximum file size in bytes for automatic ACC upload / ZIP extraction.
    /// Computed from <see cref="MaxUploadFileSizeMb"/>.
    /// </summary>
    public static long MaxUploadFileSizeBytes => MaxUploadFileSizeMb * 1024 * 1024;
}
