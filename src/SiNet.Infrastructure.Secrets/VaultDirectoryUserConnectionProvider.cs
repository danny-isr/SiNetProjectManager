using SiNet.Application.Configuration;
using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Secrets;

/// <summary>
/// AD connection settings from host domain config + vault credentials.
/// </summary>
public sealed class VaultDirectoryUserConnectionProvider(
    ISecretVaultStore vault,
    ISecretSetupHostConfiguration hostConfiguration) : IDirectoryUserConnectionProvider
{
    private readonly ISecretVaultStore _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    private readonly ISecretSetupHostConfiguration _hostConfiguration =
        hostConfiguration ?? throw new ArgumentNullException(nameof(hostConfiguration));

    public DirectoryUserConnectionSettings GetConnectionSettings()
    {
        var domain = _hostConfiguration.ActiveDirectoryDomainName;

        return new DirectoryUserConnectionSettings
        {
            DomainName = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim(),
            Username = _vault.GetSecret(SecretCatalog.AdUsername),
            Password = _vault.GetSecret(SecretCatalog.AdPassword),
        };
    }
}
