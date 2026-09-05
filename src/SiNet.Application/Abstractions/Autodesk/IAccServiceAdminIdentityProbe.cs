using SiNet.Application.Identity;

namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>HTTP probe of AccService <c>/v1/acc/admin-identity</c> (emails / ids only; never tokens).</summary>
public interface IAccServiceAdminIdentityRemoteProbe
{
    Task<AccServiceAdminIdentityRemoteResult> ProbeAsync(CancellationToken cancellationToken = default);
}

public sealed record AccServiceAdminIdentityRemoteResult(
    bool Reachable,
    string? ExpectedAdminEmail,
    string? ActualAdminEmail,
    bool TokenAvailable,
    bool ProfileResolved,
    string? AutodeskUserId,
    string? DisplayName,
    bool EmailMatch,
    string? IdentityStatus,
    string? AdminApiStatus,
    string? FailureReason,
    string? Detail,
    string? TokenPurpose = null,
    string? TokenStoragePath = null,
    bool? TokenExists = null);
