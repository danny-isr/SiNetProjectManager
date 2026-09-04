using SiNet.App.Wpf.Shell;
using SiNet.Application.Common;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

/// <summary>
/// Shell-level pending-user proof without mutating the real employee SIUser row.
/// Uses AuthenticatedUserSession seam + NewShellViewModel (no WPF window required).
/// </summary>
public sealed class PendingShellIdentityProofTests
{
    [Fact]
    public async Task Pending_profile_opens_restricted_shell_status_and_denies_business()
    {
        var session = new AuthenticatedUserSession();
        var pending = new CurrentUserProfileDto(
            UserId: 9001,
            DisplayName: "Pending Proof",
            LoginName: @"TESTDOMAIN\pending_shell_proof",
            Role: AppRole.Unauthorized,
            IsActive: true,
            Email: null);
        session.SetAuthenticated(pending);

        Assert.True(session.IsPendingApproval);
        Assert.False(session.HasAccess);

        var coherence = new IdentityCoherenceService(session, new Passthrough(session));
        var snap = await coherence.EvaluateAsync();
        Assert.Equal(IdentityCoherenceStatus.PendingApproval, snap.Status);

        using var shellVm = new NewShellViewModel(
            menuItems: Array.Empty<NewShellMenuItem>(),
            currentUserDisplay: CurrentUserProfileDisplay.Format(pending),
            identityCoherence: coherence);
        shellVm.ApplyIdentitySnapshot(snap);

        Assert.Equal("זהות: ממתין לאישור מנהל מערכת", shellVm.IdentityStatusText);
        Assert.Empty(shellVm.MenuItems);
        Assert.False(shellVm.CanOpenNewProject);

        var guard = new IdentityOperationGuard(coherence);
        var gmail = await guard.EvaluateAsync(IdentityOperationKind.GmailWrite);
        var acc = await guard.EvaluateAsync(
            IdentityOperationKind.AccFileWrite,
            IdentityOperationContext.ForSiProject(1));
        var wf = await guard.EvaluateAsync(IdentityOperationKind.WorkflowMutate);

        Assert.False(gmail.Allowed);
        Assert.False(acc.Allowed);
        Assert.False(wf.Allowed);
    }

    private sealed class Passthrough(AuthenticatedUserSession session) : ICurrentUserSessionRefreshService
    {
        public Task<CurrentUserProfileDto?> RefreshCurrentUserAsync(CancellationToken cancellationToken = default)
            => session.GetCurrentUserAsync(cancellationToken);
    }
}
