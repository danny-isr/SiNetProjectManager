namespace SiNetSQL.Services;

/// <summary>
/// Static bridge for accessing application secrets from the credential vault.
/// Wired during app startup by the host application's <c>CredentialVaultService</c>.
/// Falls back to environment variables for backward compatibility.
/// </summary>
public static class CredentialProvider
{
    /// <summary>
    /// Delegate to retrieve a secret by key. Null when vault is not wired.
    /// Set by the host application during startup.
    /// </summary>
    public static Func<string, string?>? GetSecret { get; set; }

    /// <summary>
    /// Retrieves the Autodesk Client ID from the vault.
    /// Falls back to the AUTODESK_CLIENT_ID environment variable.
    /// </summary>
    public static string? AutodeskClientId =>
        GetSecret?.Invoke(SecretKeys.AutodeskClientId)
        ?? Environment.GetEnvironmentVariable("AUTODESK_CLIENT_ID");

    /// <summary>
    /// Retrieves the Autodesk Client Secret from the vault.
    /// Falls back to the AUTODESK_CLIENT_SECRET environment variable.
    /// </summary>
    public static string? AutodeskClientSecret =>
        GetSecret?.Invoke(SecretKeys.AutodeskClientSecret)
        ?? Environment.GetEnvironmentVariable("AUTODESK_CLIENT_SECRET");
}

/// <summary>
/// Well-known secret key names for the credential vault.
/// Prefix: "SiNet/" — visible in Windows Credential Manager.
/// </summary>
public static class SecretKeys
{
    public const string GeminiApiKey = "SiNet/GeminiApiKey";
    public const string AutodeskClientId = "SiNet/Autodesk/ClientId";
    public const string AutodeskClientSecret = "SiNet/Autodesk/ClientSecret";
    public const string GoogleClientSecrets = "SiNet/Google/ClientSecrets";

    // Active Directory
    public const string AdUsername = "SiNet/ActiveDirectory/Username";
    public const string AdPassword = "SiNet/ActiveDirectory/Password";

    // Connection strings
    public const string ConnectionStringPrefix = "SiNet/ConnectionStrings/";
    public const string SiNetDatabase = "SiNet/ConnectionStrings/SiNetDatabase";
    public const string ReplicaDatabase = "SiNet/ConnectionStrings/ReplicaDatabase";
    public const string MasterPlanDatabase = "SiNet/ConnectionStrings/MasterPlanDatabase";

    // SiOffice.AccService — privileged-operations service shared API key.
    // Same value on server (validates header) and clients (sends header).
    public const string AccServiceApiKey = "SiNet/AccService/ApiKey";

    // MasterPlan Web API — X-API-Key header used by MasterPlan.SyncEngine.
    public const string MasterPlanApiKey = "SiNet/MasterPlanApi/ApiKey";

    /// <summary>All secret keys for vault status checking.</summary>
    public static readonly string[] All =
    [
        GeminiApiKey,
        AutodeskClientId,
        AutodeskClientSecret,
        GoogleClientSecrets,
        AdUsername,
        AdPassword,
        SiNetDatabase,
        ReplicaDatabase,
        MasterPlanDatabase,
        AccServiceApiKey,
        MasterPlanApiKey
    ];
}
