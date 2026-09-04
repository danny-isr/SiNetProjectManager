using SiNet.Application.Identity;
using SiNetSQL.Services.AccBootstrap;

namespace SiNet.Infrastructure.AccBootstrap;

/// <summary>
/// Production ACC human-membership probe: lists members from Autodesk/AccService,
/// optionally reconciles once, then re-reads. SQL is used only to resolve AccProjectId.
/// </summary>
public sealed class AccHumanMembershipProbe(
    IAccProjectProvisioningService provisioning,
    IAccProjectIdResolver? projectIdResolver = null) : IAccHumanMembershipProbe
{
    private readonly IAccProjectProvisioningService _provisioning =
        provisioning ?? throw new ArgumentNullException(nameof(provisioning));
    private readonly IAccProjectIdResolver? _projectIdResolver = projectIdResolver;

    /// <inheritdoc />
    public async Task<AccHumanMembershipProbeResult?> ProbeForSiProjectAsync(
        int siProjectId,
        string expectedEmail,
        bool allowReconcile = true,
        CancellationToken cancellationToken = default)
    {
        if (siProjectId <= 0 || _projectIdResolver is null)
        {
            return null;
        }

        var accProjectId = await _projectIdResolver
            .ResolveAccProjectIdAsync(siProjectId, cancellationToken)
            .ConfigureAwait(false);
        return await ProbeAsync(accProjectId, expectedEmail, allowReconcile, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AccHumanMembershipProbeResult?> ProbeAsync(
        string? accProjectId,
        string expectedEmail,
        bool allowReconcile = true,
        CancellationToken cancellationToken = default)
    {
        var email = IdentityEmailComparer.Normalize(expectedEmail);
        if (email is null || string.IsNullOrWhiteSpace(accProjectId))
        {
            return null;
        }

        accProjectId = accProjectId.Trim();

        try
        {
            var first = await FindMemberAsync(accProjectId, email, cancellationToken).ConfigureAwait(false);
            if (first is not null)
            {
                return new AccHumanMembershipProbeResult(
                    ExpectedEmail: email,
                    MatchedMemberEmail: first.Email,
                    IsMember: true,
                    ReconcileAttempted: false,
                    AccessLevel: first.AccessLevel,
                    ProbeSucceeded: true);
            }

            var reconcileAttempted = false;
            if (allowReconcile)
            {
                try
                {
                    await _provisioning.ReconcileProjectMembersAsync(accProjectId, cancellationToken)
                        .ConfigureAwait(false);
                    reconcileAttempted = true;
                }
                catch (Exception ex)
                {
                    return new AccHumanMembershipProbeResult(
                        ExpectedEmail: email,
                        MatchedMemberEmail: null,
                        IsMember: false,
                        ReconcileAttempted: true,
                        AccessLevel: null,
                        ProbeSucceeded: true,
                        FailureReason: $"ACC membership reconcile failed: {ex.Message}");
                }

                var second = await FindMemberAsync(accProjectId, email, cancellationToken).ConfigureAwait(false);
                if (second is not null)
                {
                    return new AccHumanMembershipProbeResult(
                        ExpectedEmail: email,
                        MatchedMemberEmail: second.Email,
                        IsMember: true,
                        ReconcileAttempted: reconcileAttempted,
                        AccessLevel: second.AccessLevel,
                        ProbeSucceeded: true);
                }
            }

            return new AccHumanMembershipProbeResult(
                ExpectedEmail: email,
                MatchedMemberEmail: null,
                IsMember: false,
                ReconcileAttempted: reconcileAttempted,
                AccessLevel: null,
                ProbeSucceeded: true,
                FailureReason: "SIUser.Email is not present in ACC project membership.");
        }
        catch (Exception ex)
        {
            return new AccHumanMembershipProbeResult(
                ExpectedEmail: email,
                MatchedMemberEmail: null,
                IsMember: false,
                ReconcileAttempted: false,
                AccessLevel: null,
                ProbeSucceeded: false,
                FailureReason: $"ACC membership probe failed: {ex.Message}");
        }
    }

    private async Task<AccProjectMemberInfo?> FindMemberAsync(
        string accProjectId,
        string expectedEmail,
        CancellationToken cancellationToken)
    {
        var members = await _provisioning
            .ListProjectMembersAsync(accProjectId, cancellationToken)
            .ConfigureAwait(false);

        return members.FirstOrDefault(m =>
            IdentityEmailComparer.EqualsNormalized(m.Email, expectedEmail));
    }
}
