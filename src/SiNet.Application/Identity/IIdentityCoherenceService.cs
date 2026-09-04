namespace SiNet.Application.Identity;

/// <summary>
/// Evaluates SIUser ↔ Google ↔ ACC human membership coherence.
/// Windows/runtime LoginName is diagnostic only — never compared to Google/ACC email.
/// </summary>
public interface IIdentityCoherenceService
{
    IdentityCoherenceSnapshot Current { get; }

    event Action<IdentityCoherenceSnapshot>? Changed;

    /// <summary>Re-evaluate from current session + connectors. May logout Google on mismatch.</summary>
    Task<IdentityCoherenceSnapshot> EvaluateAsync(
        IdentityCoherenceEvaluateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads the SIUser row into the session (admin approval refresh), then evaluates coherence.
    /// </summary>
    Task<IdentityCoherenceSnapshot> RefreshSiUserAndEvaluateAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Optional inputs for a coherence pass.</summary>
public sealed record IdentityCoherenceEvaluateOptions(
    bool DisconnectGoogleOnMismatch = true,
    bool ProbeAccMembership = true,
    int? SiProjectId = null,
    string? AccProjectId = null,
    string? AutodeskThreeLeggedEmail = null,
    bool AllowAccMembershipReconcile = true,
    /// <summary>When true (or when Si/Acc project ids are set), ACC is relevant for overall MATCH.</summary>
    bool? HasActiveProject = null);
