namespace SiNet.Application.Identity;

/// <summary>
/// Optional probe: does the current project's ACC membership include <c>SIUser.Email</c>?
/// Normal ACC file ops use 2-legged application OAuth — that is <b>not</b> the human identity.
/// </summary>
public interface IAccHumanMembershipProbe
{
    /// <summary>
    /// Looks up whether <paramref name="expectedEmail"/> appears in ACC project membership.
    /// Returns <see langword="null"/> when probing is unavailable (no project / no client).
    /// </summary>
    Task<AccHumanMembershipProbeResult?> ProbeAsync(
        string? accProjectId,
        string expectedEmail,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of an ACC human membership probe (never tokens).</summary>
public sealed record AccHumanMembershipProbeResult(
    string? MatchedMemberEmail,
    bool IsMember,
    bool ReconcileAttempted);
