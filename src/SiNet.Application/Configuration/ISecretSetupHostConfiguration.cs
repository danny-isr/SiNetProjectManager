namespace SiNet.Application.Configuration;

/// <summary>Non-secret host settings required for secret validation (bound by the host).</summary>
public interface ISecretSetupHostConfiguration
{
    string? ActiveDirectoryDomainName { get; }
}
