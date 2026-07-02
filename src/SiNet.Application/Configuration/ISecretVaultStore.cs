namespace SiNet.Application.Configuration;

/// <summary>Abstraction over Windows Credential Manager (or test doubles).</summary>
public interface ISecretVaultStore
{
    bool HasSecret(string key);

    string? GetSecret(string key);

    void SetSecret(string key, string value);

    IReadOnlyDictionary<string, bool> GetVaultStatus();
}
