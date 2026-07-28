using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Secrets;

public static class SecretsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Credential Vault module. Idempotent: <c>AddSiNet</c> calls this, and hosts that
    /// need the vault before the full graph exists (bootstrap providers) call it again.
    /// </summary>
    public static IServiceCollection AddSiNetSecrets(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(d => d.ServiceType == typeof(ISecretSetupHostConfiguration)))
        {
            services.AddSingleton<ISecretSetupHostConfiguration>(NullSecretSetupHostConfiguration.Instance);
        }

        if (!services.Any(d => d.ServiceType == typeof(GoogleClientSecretsFallbackOptions)))
        {
            services.AddSingleton(new GoogleClientSecretsFallbackOptions());
        }

        services.TryAddSingleton<ISecretVaultStore, WindowsCredentialVaultStore>();
        services.TryAddSingleton<IGoogleClientSecretsMaterializer, GoogleClientSecretsMaterializer>();
        services.TryAddSingleton<IGoogleClientSecretsPathProvider, VaultGoogleClientSecretsPathProvider>();
        services.TryAddSingleton<ISecretSetupService, CredentialVaultSecretSetupService>();

        return services;
    }
}

internal sealed class NullSecretSetupHostConfiguration : ISecretSetupHostConfiguration
{
    public static NullSecretSetupHostConfiguration Instance { get; } = new();

    public string? ActiveDirectoryDomainName => null;

    public string? AccServiceBaseUrl => null;
}
