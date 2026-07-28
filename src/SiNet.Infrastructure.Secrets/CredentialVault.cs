namespace SiNet.Infrastructure.Secrets;

/// <summary>
/// Pre-DI / static access to the Windows Credential Manager vault used by
/// <see cref="ISecretVaultStore"/>. Same <c>SiNet/*</c> targets as legacy
/// <c>CredentialVaultService</c> (LocalMachine persist).
/// </summary>
public static class CredentialVault
{
    public static bool HasSecret(string key) => WindowsCredentialManagerVault.HasSecret(key);

    public static string? GetSecret(string key) => WindowsCredentialManagerVault.GetSecret(key);

    public static void SetSecret(string key, string value) =>
        WindowsCredentialManagerVault.SetSecret(key, value);

    public static IReadOnlyDictionary<string, bool> GetVaultStatus() =>
        WindowsCredentialManagerVault.GetVaultStatus();
}
