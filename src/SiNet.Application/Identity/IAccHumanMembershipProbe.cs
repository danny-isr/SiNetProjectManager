namespace SiNet.Application.Identity;

/// <summary>
/// Optional probe: does the target ACC project's membership include <c>SIUser.Email</c>?
/// Normal ACC file ops use 2-legged application OAuth — that is <b>not</b> the human identity.
/// </summary>
public interface IAccHumanMembershipProbe
{
    /// <summary>
    /// Probes ACC membership for <paramref name="expectedEmail"/>.
    /// When <paramref name="allowReconcile"/> is true and the email is missing, may invoke the
    /// supported membership reconciler once and re-read ACC.
    /// Returns <see langword="null"/> only when probing is completely unavailable (no AccProjectId / no client).
    /// </summary>
    Task<AccHumanMembershipProbeResult?> ProbeAsync(
        string? accProjectId,
        string expectedEmail,
        bool allowReconcile = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves AccProjectId from SiNet project when needed, then probes.
    /// </summary>
    Task<AccHumanMembershipProbeResult?> ProbeForSiProjectAsync(
        int siProjectId,
        string expectedEmail,
        bool allowReconcile = true,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of an ACC human membership probe (never tokens).</summary>
public sealed record AccHumanMembershipProbeResult(
    string ExpectedEmail,
    string? MatchedMemberEmail,
    bool IsMember,
    bool ReconcileAttempted,
    string? AccessLevel = null,
    bool ProbeSucceeded = true,
    string? FailureReason = null);
