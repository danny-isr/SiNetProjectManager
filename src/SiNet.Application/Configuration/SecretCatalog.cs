namespace SiNet.Application.Configuration;

/// <summary>
/// Canonical list of vault keys for native Secret Setup. Strings match <c>SecretKeys</c> in SiNetSQL.
/// Credential Vault is the single source of truth for secret values.
/// </summary>
public static class SecretCatalog
{
    public const string GeminiApiKey = "SiNet/GeminiApiKey";
    public const string AutodeskClientId = "SiNet/Autodesk/ClientId";
    public const string AutodeskClientSecret = "SiNet/Autodesk/ClientSecret";
    public const string GoogleClientSecrets = "SiNet/Google/ClientSecrets";
    public const string AdUsername = "SiNet/ActiveDirectory/Username";
    public const string AdPassword = "SiNet/ActiveDirectory/Password";
    public const string SiNetDatabase = "SiNet/ConnectionStrings/SiNetDatabase";
    public const string ReplicaDatabase = "SiNet/ConnectionStrings/ReplicaDatabase";
    public const string MasterPlanDatabase = "SiNet/ConnectionStrings/MasterPlanDatabase";
    public const string AccServiceApiKey = "SiNet/AccService/ApiKey";
    public const string MasterPlanApiKey = "SiNet/MasterPlanApi/ApiKey";

    public static IReadOnlyList<SecretCatalogEntry> All { get; } =
    [
        new(GeminiApiKey, "Gemini API Key", SecretKind.Password, true, false, SecretValidationGroup.Gemini),
        new(AutodeskClientId, "Autodesk Client ID", SecretKind.Text, false, true, SecretValidationGroup.Autodesk, PairKey: AutodeskClientSecret),
        new(AutodeskClientSecret, "Autodesk Client Secret", SecretKind.Password, true, false, SecretValidationGroup.Autodesk, PairKey: AutodeskClientId),
        new(GoogleClientSecrets, "Google OAuth credentials.json", SecretKind.JsonFile, true, false, SecretValidationGroup.GoogleOAuth),
        new(SiNetDatabase, "SiNet Database", SecretKind.ConnectionString, false, true, SecretValidationGroup.Database),
        new(ReplicaDatabase, "Replica Database", SecretKind.ConnectionString, false, true, SecretValidationGroup.Database),
        new(MasterPlanDatabase, "MasterPlan Database", SecretKind.ConnectionString, false, true, SecretValidationGroup.Database),
        new(AdUsername, "Active Directory Username", SecretKind.Text, false, true, SecretValidationGroup.ActiveDirectory, PairKey: AdPassword),
        new(AdPassword, "Active Directory Password", SecretKind.Password, true, false, SecretValidationGroup.ActiveDirectory, PairKey: AdUsername),
        new(AccServiceApiKey, "AccService API Key", SecretKind.ApiKey, false, true, SecretValidationGroup.AccServiceApiKey),
        new(MasterPlanApiKey, "MasterPlan API Key", SecretKind.ApiKey, false, true, SecretValidationGroup.MasterPlanApiKey),
    ];

    public static IReadOnlyList<string> AllKeys { get; } = All.Select(e => e.Key).ToArray();
}
