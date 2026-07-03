using System.Security.Cryptography;
using System.Text;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>Describes the locally provisioned AccService API key without exposing the secret value.</summary>
public sealed class VaultAccServiceKeyDiagnostics(ISecretVaultStore vault) : IAccServiceKeyDiagnostics
{
    private readonly ISecretVaultStore _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    public AccServiceKeyInfo Describe()
    {
        if (!_vault.HasSecret(SecretCatalog.AccServiceApiKey))
        {
            return new AccServiceKeyInfo(false, 0, null);
        }

        var key = _vault.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            return new AccServiceKeyInfo(false, 0, null);
        }

        return new AccServiceKeyInfo(
            HasApiKey: true,
            KeyLength: key.Length,
            KeyHashPrefix: ComputeHashPrefix(key));
    }

    private static string ComputeHashPrefix(string key)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
    }
}
