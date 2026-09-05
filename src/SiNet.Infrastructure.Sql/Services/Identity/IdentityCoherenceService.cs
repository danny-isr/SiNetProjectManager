using SiNet.Application.Common;
using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Central SIUser ↔ Google ↔ ACC membership coherence evaluator.
/// On Google mismatch, disconnects the shared Google session (Gmail/Drive/Sheets).
/// Overall MATCH requires ACC membership readback when a project is active.
/// </summary>
public sealed class IdentityCoherenceService : IIdentityCoherenceService
{
    private readonly ICurrentUserProfileService _profiles;
    private readonly IConnectorAuthService? _googleAuth;
    private readonly ICurrentUserSessionRefreshService _sessionRefresh;
    private readonly IAccHumanMembershipProbe? _accMembership;
    private readonly IAccProjectIdResolver? _accProjectIdResolver;
    private readonly object _gate = new();
    private IdentityCoherenceSnapshot _current = IdentityCoherenceSnapshot.Checking();

    public IdentityCoherenceService(
        ICurrentUserProfileService profiles,
        ICurrentUserSessionRefreshService sessionRefresh,
        IConnectorAuthService? googleAuth = null,
        IAccHumanMembershipProbe? accMembership = null,
        IAccProjectIdResolver? accProjectIdResolver = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessionRefresh = sessionRefresh ?? throw new ArgumentNullException(nameof(sessionRefresh));
        _googleAuth = googleAuth;
        _accMembership = accMembership;
        _accProjectIdResolver = accProjectIdResolver;

        if (_googleAuth is not null)
        {
            _googleAuth.AuthStateChanged += OnGoogleAuthStateChanged;
        }
    }

    private void OnGoogleAuthStateChanged(bool authenticated)
    {
        _ = EvaluateAsync(new IdentityCoherenceEvaluateOptions(DisconnectGoogleOnMismatch: true));
    }

    /// <inheritdoc />
    public IdentityCoherenceSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public event Action<IdentityCoherenceSnapshot>? Changed;

    /// <inheritdoc />
    public async Task<IdentityCoherenceSnapshot> RefreshSiUserAndEvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        await _sessionRefresh.RefreshCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        return await EvaluateAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IdentityCoherenceSnapshot> EvaluateAsync(
        IdentityCoherenceEvaluateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new IdentityCoherenceEvaluateOptions();
        var profile = await _profiles.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Publish(Blocked("No authenticated SIUser session."));
        }

        if (!profile.IsActive)
        {
            return Publish(BuildBase(profile, IdentityCoherenceStatus.Blocked, "SIUser is inactive."));
        }

        if (profile.IsPendingApproval)
        {
            return Publish(BuildBase(profile, IdentityCoherenceStatus.PendingApproval, "Pending administrator approval."));
        }

        var siEmail = IdentityEmailComparer.Normalize(profile.Email);
        if (siEmail is null)
        {
            return Publish(BuildBase(profile, IdentityCoherenceStatus.IncompleteSiUser, "SIUser.Email is empty."));
        }

        var googleAuth = _googleAuth is { IsAuthenticated: true };
        var googleEmail = IdentityEmailComparer.Normalize(_googleAuth?.ConnectedAccountEmail);
        bool? googleMatch = null;
        string? failure = null;
        var status = IdentityCoherenceStatus.Match;

        if (!googleAuth || googleEmail is null)
        {
            status = IdentityCoherenceStatus.NotConnected;
            failure = "Google session not connected.";
            googleMatch = null;
        }
        else if (IdentityEmailComparer.EqualsNormalized(siEmail, googleEmail))
        {
            googleMatch = true;
        }
        else
        {
            googleMatch = false;
            status = IdentityCoherenceStatus.Mismatch;
            failure = "Google ConnectedAccountEmail does not match SIUser.Email.";
            if (options.DisconnectGoogleOnMismatch && _googleAuth is not null)
            {
                await _googleAuth.LogoutAsync(cancellationToken).ConfigureAwait(false);
                googleAuth = false;
            }
        }

        var accProjectId = string.IsNullOrWhiteSpace(options.AccProjectId)
            ? null
            : options.AccProjectId.Trim();
        if (string.IsNullOrWhiteSpace(accProjectId)
            && options.SiProjectId is int siPid and > 0
            && _accProjectIdResolver is not null)
        {
            accProjectId = await _accProjectIdResolver
                .ResolveAccProjectIdAsync(siPid, cancellationToken)
                .ConfigureAwait(false);
        }

        var accRelevant = options.HasActiveProject == true
            || options.SiProjectId is > 0
            || !string.IsNullOrWhiteSpace(accProjectId);

        bool? accMatch = null;
        string? accMemberEmail = null;
        string? accAccess = null;

        if (options.ProbeAccMembership && accRelevant)
        {
            if (_accMembership is null)
            {
                if (status == IdentityCoherenceStatus.Match)
                {
                    status = IdentityCoherenceStatus.AccUnverified;
                    failure = "ACC membership probe is not registered.";
                }

                accMatch = null;
            }
            else
            {
                AccHumanMembershipProbeResult? probe;
                if (!string.IsNullOrWhiteSpace(accProjectId))
                {
                    probe = await _accMembership
                        .ProbeAsync(
                            accProjectId,
                            siEmail,
                            allowReconcile: options.AllowAccMembershipReconcile,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (options.SiProjectId is int siForProbe and > 0)
                {
                    probe = await _accMembership
                        .ProbeForSiProjectAsync(
                            siForProbe,
                            siEmail,
                            allowReconcile: options.AllowAccMembershipReconcile,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (probe is not null && string.IsNullOrWhiteSpace(accProjectId)
                        && _accProjectIdResolver is not null)
                    {
                        accProjectId = await _accProjectIdResolver
                            .ResolveAccProjectIdAsync(siForProbe, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                else
                {
                    probe = null;
                }

                if (probe is null || !probe.ProbeSucceeded)
                {
                    accMatch = null;
                    if (status == IdentityCoherenceStatus.Match)
                    {
                        status = IdentityCoherenceStatus.AccUnverified;
                        failure = probe?.FailureReason
                            ?? (string.IsNullOrWhiteSpace(accProjectId) && options.SiProjectId is null
                                ? "ACC project context missing."
                                : "ACC membership probe unavailable.");
                    }
                }
                else
                {
                    accMatch = probe.IsMember;
                    accMemberEmail = probe.MatchedMemberEmail;
                    accAccess = probe.AccessLevel;
                    if (!probe.IsMember)
                    {
                        status = IdentityCoherenceStatus.Mismatch;
                        failure = failure is null
                            ? "SIUser.Email is not an ACC project member."
                            : failure + " ACC membership mismatch.";
                    }
                }
            }
        }

        // AccService Admin 3-legged credential is never compared to SIUser.Email.
        // Only UserContext 3-legged (operator acting as themselves) participates in MATCH.
        bool? threeLeggedMatch = null;
        string? threeLeggedEmail = null;
        if (options.AutodeskCredentialPurpose == AutodeskCredentialPurpose.UserContext)
        {
            threeLeggedEmail = IdentityEmailComparer.Normalize(options.AutodeskThreeLeggedEmail);
            if (threeLeggedEmail is not null)
            {
                threeLeggedMatch = IdentityEmailComparer.EqualsNormalized(siEmail, threeLeggedEmail);
                if (threeLeggedMatch == false)
                {
                    status = IdentityCoherenceStatus.Mismatch;
                    failure = failure is null
                        ? "Autodesk 3-legged email does not match SIUser.Email."
                        : failure + " Autodesk 3-legged mismatch.";
                }
            }
        }

        // Full MATCH requires Google match; when ACC is relevant, also AccMembershipMatch == true.
        if (status == IdentityCoherenceStatus.Match
            && accRelevant
            && accMatch is not true)
        {
            status = IdentityCoherenceStatus.AccUnverified;
            failure ??= "ACC membership not verified for active project.";
        }

        return Publish(new IdentityCoherenceSnapshot(
            Status: status,
            SiUserId: profile.UserId,
            SiUserName: profile.DisplayName,
            SiUserLoginName: profile.LoginName,
            SiUserEmail: siEmail,
            GoogleAuthenticated: googleAuth && googleEmail is not null,
            GoogleEmail: googleEmail,
            GoogleMatch: googleMatch,
            GmailMatch: googleMatch,
            DriveMatch: googleMatch,
            SheetsMatch: googleMatch,
            AccAuthMode: AccAuthMode.ApplicationTwoLegged,
            AccMembershipEmail: accMemberEmail,
            AccMembershipMatch: accMatch,
            AutodeskThreeLeggedEmail: threeLeggedEmail,
            AutodeskThreeLeggedMatch: threeLeggedMatch,
            FailureReason: failure,
            AccAccessLevel: accAccess,
            SiProjectId: options.SiProjectId,
            AccProjectId: accProjectId,
            AccRelevant: accRelevant));
    }

    private static IdentityCoherenceSnapshot Blocked(string failure) =>
        new(
            Status: IdentityCoherenceStatus.Blocked,
            SiUserId: null,
            SiUserName: null,
            SiUserLoginName: null,
            SiUserEmail: null,
            GoogleAuthenticated: false,
            GoogleEmail: null,
            GoogleMatch: null,
            GmailMatch: null,
            DriveMatch: null,
            SheetsMatch: null,
            AccAuthMode: AccAuthMode.ApplicationTwoLegged,
            AccMembershipEmail: null,
            AccMembershipMatch: null,
            AutodeskThreeLeggedEmail: null,
            AutodeskThreeLeggedMatch: null,
            FailureReason: failure);

    private static IdentityCoherenceSnapshot BuildBase(
        CurrentUserProfileDto profile,
        IdentityCoherenceStatus status,
        string? failure) =>
        new(
            Status: status,
            SiUserId: profile.UserId,
            SiUserName: profile.DisplayName,
            SiUserLoginName: profile.LoginName,
            SiUserEmail: IdentityEmailComparer.Normalize(profile.Email),
            GoogleAuthenticated: false,
            GoogleEmail: null,
            GoogleMatch: null,
            GmailMatch: null,
            DriveMatch: null,
            SheetsMatch: null,
            AccAuthMode: AccAuthMode.ApplicationTwoLegged,
            AccMembershipEmail: null,
            AccMembershipMatch: null,
            AutodeskThreeLeggedEmail: null,
            AutodeskThreeLeggedMatch: null,
            FailureReason: failure);

    private IdentityCoherenceSnapshot Publish(IdentityCoherenceSnapshot snapshot)
    {
        lock (_gate)
        {
            _current = snapshot;
        }

        Changed?.Invoke(snapshot);
        return snapshot;
    }
}
