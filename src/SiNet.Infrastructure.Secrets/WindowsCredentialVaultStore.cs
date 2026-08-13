using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Secrets;

internal sealed class WindowsCredentialVaultStore : ISecretVaultStore
{
    public bool HasSecret(string key) => WindowsCredentialManagerVault.HasSecret(key);

    public string? GetSecret(string key) => WindowsCredentialManagerVault.GetSecret(key);

    public void SetSecret(string key, string value) => WindowsCredentialManagerVault.SetSecret(key, value);

    public void DeleteSecret(string key) => WindowsCredentialManagerVault.DeleteSecret(key);

    public IReadOnlyDictionary<string, bool> GetVaultStatus() => WindowsCredentialManagerVault.GetVaultStatus();
}
