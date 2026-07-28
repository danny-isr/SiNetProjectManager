using Microsoft.Extensions.Configuration;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Autodesk;

namespace SiNet.App.Wpf.Infrastructure;

/// <summary>
/// Binds non-secret AccService / AD host settings from the standalone host's <see cref="IConfiguration"/>.
/// </summary>
internal sealed class ConfigurationSecretSetupHostConfiguration(IConfiguration configuration)
    : ISecretSetupHostConfiguration
{
    private readonly IConfiguration _configuration =
        configuration ?? throw new ArgumentNullException(nameof(configuration));

    public string? ActiveDirectoryDomainName => _configuration["ActiveDirectory:DomainName"];

    public string? AccServiceBaseUrl => _configuration["AccService:BaseUrl"];

    public IReadOnlyList<string> AccServicePinnedCertificateThumbprints =>
        AccServiceControlPlaneConfiguration.ReadPinnedCertificateThumbprints(_configuration);
}
