using Microsoft.Extensions.Configuration;
using SiNet.Application.Configuration;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Autodesk;

namespace SiNet.App.Wpf.Infrastructure;

/// <summary>
/// Host AccService / AD settings from appsettings, overridable at startup from system settings (DB).
/// </summary>
internal sealed class MutableSecretSetupHostConfiguration(IConfiguration configuration)
    : ISecretSetupHostConfiguration
{
    private readonly IConfiguration _configuration =
        configuration ?? throw new ArgumentNullException(nameof(configuration));

    private string? _accServiceBaseUrlOverride;
    private IReadOnlyList<string>? _pinnedThumbprintsOverride;

    public string? ActiveDirectoryDomainName =>
        _configuration["ActiveDirectory:DomainName"]
        ?? _configuration["ActiveDirectory:Domain"];

    public string? AccServiceBaseUrl =>
        !string.IsNullOrWhiteSpace(_accServiceBaseUrlOverride)
            ? _accServiceBaseUrlOverride
            : _configuration["AccService:BaseUrl"];

    public IReadOnlyList<string> AccServicePinnedCertificateThumbprints =>
        _pinnedThumbprintsOverride
        ?? AccServiceControlPlaneConfiguration.ReadPinnedCertificateThumbprints(_configuration);

    /// <summary>Applies AccService BaseUrl and TLS pins from SQL system settings.</summary>
    public void ApplySystemSettings(AccSystemSettingsDto acc)
    {
        ArgumentNullException.ThrowIfNull(acc);

        if (!string.IsNullOrWhiteSpace(acc.AccServiceBaseUrl))
        {
            _accServiceBaseUrlOverride = acc.AccServiceBaseUrl.Trim().TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(acc.AccServicePinnedCertificateThumbprints))
        {
            _pinnedThumbprintsOverride = AccServiceControlPlaneConfiguration.SplitPins(
                acc.AccServicePinnedCertificateThumbprints);
        }
    }
}
