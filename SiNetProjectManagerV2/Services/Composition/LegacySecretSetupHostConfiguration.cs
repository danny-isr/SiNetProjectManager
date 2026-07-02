using SiNet.Application.Configuration;
using SiNetProjectManagerV2.Services;

namespace SiNetProjectManagerV2.Services.Composition;

internal sealed class LegacySecretSetupHostConfiguration : ISecretSetupHostConfiguration
{
    public string? ActiveDirectoryDomainName => AppConfiguration.AdDomainName;

    public string? AccServiceBaseUrl => AppConfiguration.Configuration["AccService:BaseUrl"];
}

internal static class LegacyGoogleClientSecretsFallback
{
    public static GoogleClientSecretsFallbackOptions Create()
        => new()
        {
            GmailClientSecretsPath = AppConfiguration.Configuration["Gmail:ClientSecretsPath"],
            GoogleReportsClientSecretsPath = AppConfiguration.GoogleReports["ClientSecretsPath"],
        };
}
