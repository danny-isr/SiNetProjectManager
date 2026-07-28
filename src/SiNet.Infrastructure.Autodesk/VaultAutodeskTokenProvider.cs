using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyOffice.AutodeskConnector;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Registers <see cref="ITokenProvider"/> from Credential Vault Autodesk client credentials
/// (standalone New System host — replaces V2 <c>CredentialProvider</c> factory).
/// </summary>
public static class VaultAutodeskTokenProviderExtensions
{
    public static IServiceCollection AddSiNetAutodeskVaultTokenProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ITokenProvider>(sp =>
        {
            var vault = sp.GetRequiredService<ISecretVaultStore>();
            var clientId = vault.GetSecret(SecretCatalog.AutodeskClientId) ?? string.Empty;
            var clientSecret = vault.GetSecret(SecretCatalog.AutodeskClientSecret) ?? string.Empty;
            return new TokenProvider(clientId, clientSecret);
        });

        return services;
    }
}
