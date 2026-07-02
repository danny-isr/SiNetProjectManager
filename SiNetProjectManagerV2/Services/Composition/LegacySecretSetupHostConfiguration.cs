using SiNet.Application.Configuration;
using SiNetProjectManagerV2.Services;

namespace SiNetProjectManagerV2.Services.Composition;

/// <summary>Host adapter: AD domain name for native secret validation.</summary>
internal sealed class LegacySecretSetupHostConfiguration : ISecretSetupHostConfiguration
{
    public string? ActiveDirectoryDomainName => AppConfiguration.AdDomainName;
}
