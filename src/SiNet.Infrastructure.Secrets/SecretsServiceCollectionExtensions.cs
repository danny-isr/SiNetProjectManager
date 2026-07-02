using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Secrets;

public static class SecretsServiceCollectionExtensions
{
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

        services.AddSingleton<ISecretVaultStore, WindowsCredentialVaultStore>();
        services.AddSingleton<IGoogleClientSecretsMaterializer, GoogleClientSecretsMaterializer>();
        services.AddSingleton<IGoogleClientSecretsPathProvider, VaultGoogleClientSecretsPathProvider>();
        services.AddSingleton<ISecretSetupService, CredentialVaultSecretSetupService>();

        return services;
    }
}

internal sealed class NullSecretSetupHostConfiguration : ISecretSetupHostConfiguration
{
    public static NullSecretSetupHostConfiguration Instance { get; } = new();

    public string? ActiveDirectoryDomainName => null;

    public string? AccServiceBaseUrl => null;
}
