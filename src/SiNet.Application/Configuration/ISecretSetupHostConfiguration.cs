namespace SiNet.Application.Configuration;

/// <summary>Non-secret host settings required for secret validation (bound by the host).</summary>
public interface ISecretSetupHostConfiguration
{
    string? ActiveDirectoryDomainName { get; }

    string? AccServiceBaseUrl { get; }

    /// <summary>
    /// TLS thumbprint pins for AccService self-signed certificates (from System Settings / host config).
    /// </summary>
    IReadOnlyList<string> AccServicePinnedCertificateThumbprints { get; }
}
