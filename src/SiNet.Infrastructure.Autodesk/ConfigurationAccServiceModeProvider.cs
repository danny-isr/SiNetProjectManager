using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>Resolves ACC runtime mode from the host's existing secret-setup configuration seam.</summary>
public sealed class ConfigurationAccServiceModeProvider(ISecretSetupHostConfiguration hostConfiguration)
    : IAccServiceModeProvider
{
    private readonly ISecretSetupHostConfiguration _hostConfiguration = hostConfiguration
        ?? throw new ArgumentNullException(nameof(hostConfiguration));

    public AccServiceMode Mode => string.IsNullOrWhiteSpace(BaseUrl) ? AccServiceMode.Local : AccServiceMode.Remote;

    public string? BaseUrl
    {
        get
        {
            var value = _hostConfiguration.AccServiceBaseUrl?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.TrimEnd('/');
        }
    }
}
