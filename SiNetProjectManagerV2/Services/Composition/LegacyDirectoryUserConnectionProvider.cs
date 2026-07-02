using SiNet.Application.Identity;
using SiNetProjectManagerV2.Services;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services.Composition;

/// <summary>
/// Host adapter: binds Active Directory settings from appsettings + credential vault for native lookup.
/// </summary>
internal sealed class LegacyDirectoryUserConnectionProvider : IDirectoryUserConnectionProvider
{
    public DirectoryUserConnectionSettings GetConnectionSettings()
        => new()
        {
            DomainName = AppConfiguration.AdDomainName,
            Username = CredentialVaultService.GetSecret(SecretKeys.AdUsername),
            Password = CredentialVaultService.GetSecret(SecretKeys.AdPassword),
        };
}
