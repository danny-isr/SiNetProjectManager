using SiNet.Application.Configuration;
using SiNetSQL.Services;

namespace SiNet.Infrastructure.Secrets;

internal sealed class WindowsCredentialVaultStore : ISecretVaultStore
{
    public bool HasSecret(string key) => CredentialVaultService.HasSecret(key);

    public string? GetSecret(string key) => CredentialVaultService.GetSecret(key);

    public void SetSecret(string key, string value) => CredentialVaultService.SetSecret(key, value);

    public IReadOnlyDictionary<string, bool> GetVaultStatus() => CredentialVaultService.GetVaultStatus();
}
