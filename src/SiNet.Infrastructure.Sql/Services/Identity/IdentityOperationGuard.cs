using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>Fail-closed identity gate before connector / business writes.</summary>
public sealed class IdentityOperationGuard(IIdentityCoherenceService coherence) : IIdentityOperationGuard
{
    private readonly IIdentityCoherenceService _coherence =
        coherence ?? throw new ArgumentNullException(nameof(coherence));

    /// <inheritdoc />
    public async Task EnsureAllowedAsync(IdentityOperationKind kind, CancellationToken cancellationToken = default)
    {
        var decision = await EvaluateAsync(kind, cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed)
        {
            throw new IdentityOperationDeniedException(
                decision.Reason ?? "Identity operation denied.",
                decision.Snapshot);
        }
    }

    /// <inheritdoc />
    public async Task<IdentityGuardDecision> EvaluateAsync(
        IdentityOperationKind kind,
        CancellationToken cancellationToken = default)
    {
        // Writes must never proceed on a known mismatch — re-evaluate before side effects.
        var probeAcc = kind is IdentityOperationKind.AccProjectMembershipWrite
            or IdentityOperationKind.AccFileWrite
            or IdentityOperationKind.CrossSystemWorkflow;

        var snapshot = await _coherence.EvaluateAsync(
                new IdentityCoherenceEvaluateOptions(
                    DisconnectGoogleOnMismatch: kind is IdentityOperationKind.GmailWrite
                        or IdentityOperationKind.GoogleDriveWrite
                        or IdentityOperationKind.GoogleSheetsWrite
                        or IdentityOperationKind.CrossSystemWorkflow,
                    ProbeAccMembership: probeAcc),
                cancellationToken)
            .ConfigureAwait(false);

        if (snapshot.Status is IdentityCoherenceStatus.PendingApproval)
        {
            return Deny(snapshot, "Pending administrator approval — business operations are blocked.");
        }

        if (snapshot.Status is IdentityCoherenceStatus.Blocked)
        {
            return Deny(snapshot, "SIUser session is blocked.");
        }

        if (snapshot.Status is IdentityCoherenceStatus.IncompleteSiUser
            || string.IsNullOrWhiteSpace(snapshot.SiUserEmail))
        {
            return Deny(snapshot, "SIUser.Email is required before connector operations.");
        }

        return kind switch
        {
            IdentityOperationKind.GmailWrite
                or IdentityOperationKind.GoogleDriveWrite
                or IdentityOperationKind.GoogleSheetsWrite
                => RequireGoogleMatch(snapshot),

            IdentityOperationKind.AccProjectMembershipWrite
                or IdentityOperationKind.AccFileWrite
                => RequireAccMembershipWhenProbed(snapshot),

            IdentityOperationKind.AutodeskThreeLeggedWrite
                => RequireThreeLeggedMatch(snapshot),

            IdentityOperationKind.WorkflowMutate
                or IdentityOperationKind.ProjectMutate
                or IdentityOperationKind.AdminSettingsWrite
                => RequireAuthorized(snapshot),

            IdentityOperationKind.CrossSystemWorkflow
                => RequireGoogleAndAcc(snapshot),

            _ => Deny(snapshot, $"Unknown identity operation kind '{kind}'."),
        };
    }

    private static IdentityGuardDecision RequireAuthorized(IdentityCoherenceSnapshot snapshot)
    {
        if (snapshot.Status is IdentityCoherenceStatus.PendingApproval
            or IdentityCoherenceStatus.Blocked
            or IdentityCoherenceStatus.IncompleteSiUser)
        {
            return Deny(snapshot, snapshot.FailureReason ?? "User is not authorized for business operations.");
        }

        // Workflow/project mutate require authorized SIUser; Google may be NotConnected for non-connector ops.
        if (snapshot.SiUserId is null or <= 0)
        {
            return Deny(snapshot, "No SIUser session.");
        }

        return Allow(snapshot);
    }

    private static IdentityGuardDecision RequireGoogleMatch(IdentityCoherenceSnapshot snapshot)
    {
        if (snapshot.Status is IdentityCoherenceStatus.Mismatch || snapshot.GoogleMatch == false)
        {
            return Deny(snapshot, snapshot.FailureReason ?? "Google identity mismatch.");
        }

        if (snapshot.Status is IdentityCoherenceStatus.NotConnected || snapshot.GoogleMatch is not true)
        {
            return Deny(snapshot, "Google session must be connected with matching SIUser.Email.");
        }

        return Allow(snapshot);
    }

    private static IdentityGuardDecision RequireAccMembershipWhenProbed(IdentityCoherenceSnapshot snapshot)
    {
        var auth = RequireAuthorized(snapshot);
        if (!auth.Allowed)
        {
            return auth;
        }

        if (snapshot.AccMembershipMatch == false)
        {
            return Deny(snapshot, "ACC project membership does not include SIUser.Email.");
        }

        // When membership was not probed (null), still allow 2-legged file plumbing only if
        // SIUser is authorized + Email populated — human ACC mismatch is fail-closed when probed.
        return Allow(snapshot);
    }

    private static IdentityGuardDecision RequireThreeLeggedMatch(IdentityCoherenceSnapshot snapshot)
    {
        var auth = RequireAuthorized(snapshot);
        if (!auth.Allowed)
        {
            return auth;
        }

        if (snapshot.AutodeskThreeLeggedMatch == false)
        {
            return Deny(snapshot, "Autodesk 3-legged email does not match SIUser.Email.");
        }

        if (snapshot.AutodeskThreeLeggedMatch is null)
        {
            return Deny(snapshot, "Autodesk 3-legged identity was not verified.");
        }

        return Allow(snapshot);
    }

    private static IdentityGuardDecision RequireGoogleAndAcc(IdentityCoherenceSnapshot snapshot)
    {
        var google = RequireGoogleMatch(snapshot);
        if (!google.Allowed)
        {
            return google;
        }

        return RequireAccMembershipWhenProbed(snapshot);
    }

    private static IdentityGuardDecision Allow(IdentityCoherenceSnapshot snapshot) =>
        new(true, null, snapshot);

    private static IdentityGuardDecision Deny(IdentityCoherenceSnapshot snapshot, string reason) =>
        new(false, reason, snapshot);
}
