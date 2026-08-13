namespace SiNet.Application.Configuration;

/// <summary>Abstraction over Windows Credential Manager (or test doubles).</summary>
public interface ISecretVaultStore
{
    bool HasSecret(string key);

    string? GetSecret(string key);

    void SetSecret(string key, string value);

    /// <summary>Removes a catalog key from this Windows user's vault. No-op if the key is absent.</summary>
    void DeleteSecret(string key);

    IReadOnlyDictionary<string, bool> GetVaultStatus();
}
