using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Common;
using SiNet.Application.Email.Acc;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.Email.Acc;
using SiNet.Infrastructure.Sql.Services.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

public sealed class MoveToProjectIdentityGuardTests
{
    [Fact]
    public async Task MoveToProject_member_false_never_calls_executor()
    {
        var executor = new SpyExecutor();
        var coordinator = BuildCoordinator(executor, member: false);

        var result = await coordinator.MoveAsync(new EmailMoveToProjectCommand(1, 100, null));

        Assert.Equal(EmailMoveToProjectOutcome.Failed, result.Outcome);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task MoveToProject_member_true_calls_executor()
    {
        var executor = new SpyExecutor();
        var coordinator = BuildCoordinator(executor, member: true);

        var result = await coordinator.MoveAsync(new EmailMoveToProjectCommand(1, 100, null));

        Assert.Equal(EmailMoveToProjectOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, executor.CallCount);
    }

    private static EmailMoveToProjectCoordinator BuildCoordinator(SpyExecutor executor, bool member)
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            1, "User", "DOMAIN\\user", AppRole.Employee, true,
            Email: "user@si.co.il"));
        var auth = new StubAuth { IsAuthenticated = true, ConnectedAccountEmail = "user@si.co.il" };
        var probe = new StubProbe
        {
            Result = new AccHumanMembershipProbeResult(
                "user@si.co.il",
                member ? "user@si.co.il" : null,
                member,
                false,
                AccessLevel: member ? "member" : null),
        };
        var resolver = new StubResolver { AccProjectId = "acc-test-project" };
        var coherence = new IdentityCoherenceService(session, new Passthrough(session), auth, probe, resolver);
        var guard = new IdentityOperationGuard(coherence);
        return new EmailMoveToProjectCoordinator(executor, new NullLogger(), guard);
    }

    private sealed class StubResolver : IAccProjectIdResolver
    {
        public string? AccProjectId { get; set; }

        public Task<string?> ResolveAccProjectIdAsync(int siProjectId, CancellationToken cancellationToken = default)
            => Task.FromResult(AccProjectId);
    }

    private sealed class SpyExecutor : IEmailMoveToProjectExecutor
    {
        public int CallCount { get; private set; }

        public Task<EmailMoveToProjectCoordinatorResult> MoveAsync(
            EmailMoveToProjectCommand command,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new EmailMoveToProjectCoordinatorResult(
                EmailMoveToProjectOutcome.Succeeded,
                "ok",
                MovedCount: 1,
                TotalCount: 1));
        }
    }

    private sealed class StubProbe : IAccHumanMembershipProbe
    {
        public AccHumanMembershipProbeResult? Result { get; set; }

        public Task<AccHumanMembershipProbeResult?> ProbeAsync(
            string? accProjectId, string expectedEmail, bool allowReconcile = true,
            CancellationToken cancellationToken = default) => Task.FromResult(Result);

        public Task<AccHumanMembershipProbeResult?> ProbeForSiProjectAsync(
            int siProjectId, string expectedEmail, bool allowReconcile = true,
            CancellationToken cancellationToken = default) => Task.FromResult(Result);
    }

    private sealed class Passthrough(AuthenticatedUserSession session) : ICurrentUserSessionRefreshService
    {
        public Task<CurrentUserProfileDto?> RefreshCurrentUserAsync(CancellationToken cancellationToken = default)
            => session.GetCurrentUserAsync(cancellationToken);
    }

    private sealed class StubAuth : IConnectorAuthService
    {
        public bool IsAuthenticated { get; set; }
        public string? ConnectedAccountEmail { get; set; }
        public event Action<bool>? AuthStateChanged;
        public Task<bool> LoginAsync(ConnectorLoginOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        public void Logout() { IsAuthenticated = false; ConnectedAccountEmail = null; AuthStateChanged?.Invoke(false); }
        public Task LogoutAsync(CancellationToken cancellationToken = default) { Logout(); return Task.CompletedTask; }
        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(IsAuthenticated);
        public Task RefreshAccountProfileAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
