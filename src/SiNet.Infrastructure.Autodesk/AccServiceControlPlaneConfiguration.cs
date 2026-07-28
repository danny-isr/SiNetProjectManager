using Microsoft.Extensions.Configuration;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Single place that reads ACC control-plane settings out of <c>IConfiguration</c>.
/// </summary>
/// <remarks>
/// Before this existed each host parsed <c>AccService:PinnedCertificateThumbprints</c> on its own,
/// and the HTTP clients created inside <c>AddSiNetAutodesk</c> got an empty pin list - so a pinned
/// internal certificate worked for legacy provisioning clients but failed for health/diag/browse.
/// </remarks>
public static class AccServiceControlPlaneConfiguration
{
    public const string BaseUrlKey = "AccService:BaseUrl";

    public const string PinnedCertificateThumbprintsKey = "AccService:PinnedCertificateThumbprints";

    /// <summary>
    /// Reads the configured TLS thumbprint pins. Returns an empty list when configuration is absent.
    /// </summary>
    public static IReadOnlyList<string> ReadPinnedCertificateThumbprints(IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return [];
        }

        return configuration
            .GetSection(PinnedCertificateThumbprintsKey)
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
    }

    /// <summary>
    /// Applies every configuration-driven control-plane setting onto <paramref name="options"/>.
    /// </summary>
    public static void Bind(AccServiceControlPlaneOptions options, IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (configuration is null)
        {
            return;
        }

        options.PinnedCertificateThumbprints = ReadPinnedCertificateThumbprints(configuration);
    }
}
