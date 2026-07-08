namespace SiNet.Application.Email.Acc;

/// <summary>
/// Ensures the legacy <c>GoogleService</c> Gmail session is ready for ACC ingest
/// (separate from the new <see cref="SiNet.Application.Common.IConnectorAuthService"/> path).
/// </summary>
public interface IGoogleIngestSessionEnsurer
{
    Task<bool> EnsureAuthenticatedForAccIngestAsync(CancellationToken cancellationToken = default);
}
