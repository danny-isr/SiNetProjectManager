using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Common;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Sql.Data;
using SiNet.Infrastructure.Sql.Entities;
using SiNet.Infrastructure.Sql.Services.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Identity;

public sealed class IdentityEmailComparerTests
{
    [Theory]
    [InlineData("a@b.com", "A@B.COM", true)]
    [InlineData(" a@b.com ", "a@b.com", true)]
    [InlineData("a@b.com", "other@b.com", false)]
    [InlineData(null, "a@b.com", false)]
    [InlineData("a@b.com", null, false)]
    public void EqualsNormalized_trims_and_ignores_case(string? left, string? right, bool expected)
        => Assert.Equal(expected, IdentityEmailComparer.EqualsNormalized(left, right));
}

public sealed class IdentityCoherenceAndGuardTests
{
    [Fact]
    public async Task Google_match_case_insensitive_sets_shared_gmail_drive_sheets()
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            1, "Danny", "DOMAIN\\danny", AppRole.Employee, true,
            Email: "Danny@Si.Co.Il"));
        var auth = new StubConnectorAuth { IsAuthenticated = true, ConnectedAccountEmail = "danny@si.co.il" };
        var coherence = new IdentityCoherenceService(session, new NoOpRefresh(session), auth);

        var snap = await coherence.EvaluateAsync(new IdentityCoherenceEvaluateOptions(DisconnectGoogleOnMismatch: false));

        Assert.Equal(IdentityCoherenceStatus.Match, snap.Status);
        Assert.True(snap.GoogleMatch);
        Assert.True(snap.GmailMatch);
        Assert.True(snap.DriveMatch);
        Assert.True(snap.SheetsMatch);
        Assert.Equal(AccAuthMode.ApplicationTwoLegged, snap.AccAuthMode);
    }

    [Fact]
    public async Task Google_mismatch_logs_out_and_denies_gmail_write()
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            1, "Danny", "DOMAIN\\danny", AppRole.Employee, true,
            Email: "danny@si.co.il"));
        var auth = new StubConnectorAuth { IsAuthenticated = true, ConnectedAccountEmail = "wrong@si.co.il" };
        var coherence = new IdentityCoherenceService(session, new NoOpRefresh(session), auth);
        var guard = new IdentityOperationGuard(coherence);

        var decision = await guard.EvaluateAsync(IdentityOperationKind.GmailWrite);

        Assert.False(decision.Allowed);
        Assert.Equal(IdentityCoherenceStatus.Mismatch, decision.Snapshot.Status);
        Assert.False(auth.IsAuthenticated);
        Assert.True(auth.LogoutCalled);
    }

    [Fact]
    public async Task Pending_user_denied_business_and_gmail()
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            9, "New", "DOMAIN\\new", AppRole.Unauthorized, true));
        var coherence = new IdentityCoherenceService(session, new NoOpRefresh(session));
        var guard = new IdentityOperationGuard(coherence);

        var gmail = await guard.EvaluateAsync(IdentityOperationKind.GmailWrite);
        var wf = await guard.EvaluateAsync(IdentityOperationKind.WorkflowMutate);

        Assert.False(gmail.Allowed);
        Assert.False(wf.Allowed);
        Assert.Equal(IdentityCoherenceStatus.PendingApproval, gmail.Snapshot.Status);
    }

    [Fact]
    public async Task Missing_SiUser_email_blocks_connectors()
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            1, "Danny", "DOMAIN\\danny", AppRole.Employee, true, Email: null));
        var auth = new StubConnectorAuth { IsAuthenticated = true, ConnectedAccountEmail = "danny@si.co.il" };
        var coherence = new IdentityCoherenceService(session, new NoOpRefresh(session), auth);
        var guard = new IdentityOperationGuard(coherence);

        var decision = await guard.EvaluateAsync(IdentityOperationKind.GmailWrite);

        Assert.False(decision.Allowed);
        Assert.Equal(IdentityCoherenceStatus.IncompleteSiUser, decision.Snapshot.Status);
    }

    [Fact]
    public async Task AccFileWrite_member_true_allowed()
    {
        var (guard, _) = CreateAccGuard(member: true);

        var decision = await guard.EvaluateAsync(
            IdentityOperationKind.AccFileWrite,
            IdentityOperationContext.ForAccProject("acc-1"));

        Assert.True(decision.Allowed);
        Assert.True(decision.Snapshot.AccMembershipMatch);
        Assert.Equal(IdentityCoherenceStatus.Match, decision.Snapshot.Status);
    }

    [Fact]
    public async Task AccFileWrite_member_false_denied()
    {
        var (guard, _) = CreateAccGuard(member: false);

        var decision = await guard.EvaluateAsync(
            IdentityOperationKind.AccFileWrite,
            IdentityOperationContext.ForAccProject("acc-1"));

        Assert.False(decision.Allowed);
        Assert.False(decision.Snapshot.AccMembershipMatch);
    }

    [Fact]
    public async Task AccFileWrite_probe_unavailable_denied()
    {
        var (guard, _) = CreateAccGuard(member: null, probeSucceeded: false);

        var decision = await guard.EvaluateAsync(
            IdentityOperationKind.AccFileWrite,
            IdentityOperationContext.ForAccProject("acc-1"));

        Assert.False(decision.Allowed);
        Assert.Null(decision.Snapshot.AccMembershipMatch);
    }

    [Fact]
    public async Task AccFileWrite_no_project_context_denied()
    {
        var (guard, _) = CreateAccGuard(member: true);

        var decision = await guard.EvaluateAsync(IdentityOperationKind.AccFileWrite);

        Assert.False(decision.Allowed);
        Assert.Contains("SiProjectId", decision.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_project_without_acc_verification_is_not_full_match()
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            1, "Danny", "DOMAIN\\danny", AppRole.Employee, true,
            Email: "danny@si.co.il"));
        var auth = new StubConnectorAuth { IsAuthenticated = true, ConnectedAccountEmail = "danny@si.co.il" };
        var coherence = new IdentityCoherenceService(session, new NoOpRefresh(session), auth);

        var snap = await coherence.EvaluateAsync(new IdentityCoherenceEvaluateOptions(
            DisconnectGoogleOnMismatch: false,
            ProbeAccMembership: true,
            HasActiveProject: true,
            SiProjectId: 3213));

        Assert.Equal(IdentityCoherenceStatus.AccUnverified, snap.Status);
        Assert.Contains("ACC: טרם אומת", IdentityStatusDisplay.FormatFooter(snap), StringComparison.Ordinal);
        Assert.DoesNotContain("זהות: תקינה", IdentityStatusDisplay.FormatFooter(snap), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Acc_two_legged_mode_is_not_human_mismatch()
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            1, "Danny", "DOMAIN\\danny", AppRole.Employee, true,
            Email: "danny@si.co.il"));
        var auth = new StubConnectorAuth { IsAuthenticated = true, ConnectedAccountEmail = "danny@si.co.il" };
        var coherence = new IdentityCoherenceService(session, new NoOpRefresh(session), auth);
        var snap = await coherence.EvaluateAsync(new IdentityCoherenceEvaluateOptions(
            ProbeAccMembership: false,
            HasActiveProject: false));

        Assert.Equal(IdentityCoherenceStatus.Match, snap.Status);
        Assert.Equal(AccAuthMode.ApplicationTwoLegged, snap.AccAuthMode);
        Assert.Null(snap.AccMembershipMatch);
        Assert.False(snap.AccRelevant);
    }

    private static (IdentityOperationGuard Guard, StubAccProbe Probe) CreateAccGuard(
        bool? member,
        bool probeSucceeded = true,
        string? expectedAccServiceAdminEmail = null)
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            1, "User", "DOMAIN\\user", AppRole.Employee, true,
            Email: "user@si.co.il"));
        var auth = new StubConnectorAuth { IsAuthenticated = true, ConnectedAccountEmail = "user@si.co.il" };
        AccHumanMembershipProbeResult? result = member is null && !probeSucceeded
            ? new AccHumanMembershipProbeResult(
                "user@si.co.il", null, false, false, ProbeSucceeded: false, FailureReason: "unavailable")
            : member is null
                ? null
                : new AccHumanMembershipProbeResult(
                    "user@si.co.il",
                    member == true ? "user@si.co.il" : null,
                    member == true,
                    ReconcileAttempted: false,
                    AccessLevel: member == true ? "member" : null,
                    ProbeSucceeded: true);
        var probe = new StubAccProbe { Result = result };
        var coherence = new IdentityCoherenceService(session, new NoOpRefresh(session), auth, probe);
        ISystemSettingsQueryService? settings = expectedAccServiceAdminEmail is null
            ? null
            : new StubExpectedAdminSettings(expectedAccServiceAdminEmail);
        return (new IdentityOperationGuard(coherence, settings), probe);
    }

    private sealed class StubExpectedAdminSettings(string expectedAdminEmail) : ISystemSettingsQueryService
    {
        public Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default)
        {
            var dto = new SystemSettingsDto(
                new EmailOfficeSystemSettingsDto("", "", "", "", null, 10),
                new AccSystemSettingsDto("", "", expectedAdminEmail, "", ""),
                new InspectionSystemSettingsDto("", "", "", ""),
                new InspectionStatusLabelsDto("", "", "", ""),
                new AiSystemSettingsDto("", "", new AiModelLevelSelectionDto("", ""), new AiModelLevelSelectionDto("", ""), new AiModelLevelSelectionDto("", ""), new AiModelLevelSelectionDto("", ""), ""),
                new CentralLoggingSettingsDto(null, 14, 90, new AppLogLevelsDto(LogLevelDto.Error, LogLevelDto.Warning), new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning), new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning), false),
                new WorkflowSystemSettingsDto(5, false, "", ""),
                new ProjectWorkSystemSettingsDto(""),
                new DiagnosticsSystemSettingsDto("", "", 7, 30));
            return Task.FromResult(dto);
        }
    }

    [Fact]
    public async Task Three_legged_mismatch_denied()
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            1, "Danny", "DOMAIN\\danny", AppRole.Employee, true,
            Email: "danny@si.co.il"));
        var auth = new StubConnectorAuth { IsAuthenticated = true, ConnectedAccountEmail = "danny@si.co.il" };
        var coherence = new IdentityCoherenceService(session, new NoOpRefresh(session), auth);
        var guard = new IdentityOperationGuard(coherence);

        var decision = await guard.EvaluateAsync(
            IdentityOperationKind.AutodeskThreeLeggedWrite,
            new IdentityOperationContext(AutodeskThreeLeggedEmail: "other@autodesk.com"));

        Assert.False(decision.Allowed);
        Assert.False(decision.Snapshot.AutodeskThreeLeggedMatch);
    }

    [Fact]
    public async Task AccServiceAdmin_email_differs_from_SIUser_does_not_break_operator_match()
    {
        // Operator: user@si.co.il (SIUser + Google + ACC member)
        // AccService Admin credential equals AccBootstrapAdminEmail (different from SIUser) — operator MATCH preserved.
        var (guard, _) = CreateAccGuard(member: true, expectedAccServiceAdminEmail: "different-admin@company");

        var membershipWrite = await guard.EvaluateAsync(
            IdentityOperationKind.AccProjectMembershipWrite,
            new IdentityOperationContext(
                AccProjectId: "acc-1",
                AutodeskThreeLeggedEmail: "different-admin@company",
                AutodeskCredentialPurpose: AutodeskCredentialPurpose.AccServiceAdmin));

        Assert.True(membershipWrite.Allowed);
        Assert.Equal(IdentityCoherenceStatus.Match, membershipWrite.Snapshot.Status);
        Assert.True(membershipWrite.Snapshot.AccMembershipMatch);
        Assert.Null(membershipWrite.Snapshot.AutodeskThreeLeggedMatch);

        var fileWrite = await guard.EvaluateAsync(
            IdentityOperationKind.AccFileWrite,
            IdentityOperationContext.ForAccProject("acc-1"));
        Assert.True(fileWrite.Allowed);
        Assert.Equal(IdentityCoherenceStatus.Match, fileWrite.Snapshot.Status);

        // User-context 3-legged with a different Autodesk email still denies.
        var userThreeLegged = await guard.EvaluateAsync(
            IdentityOperationKind.AutodeskThreeLeggedWrite,
            IdentityOperationContext.ForUserThreeLegged("different-admin@company"));
        Assert.False(userThreeLegged.Allowed);
    }

    [Fact]
    public async Task AccServiceAdmin_known_wrong_admin_fail_closed_on_membership_write()
    {
        var (guard, _) = CreateAccGuard(member: true, expectedAccServiceAdminEmail: "siad@si-eng.co.il");

        var decision = await guard.EvaluateAsync(
            IdentityOperationKind.AccProjectMembershipWrite,
            new IdentityOperationContext(
                AccProjectId: "acc-1",
                AutodeskThreeLeggedEmail: "danny@si-eng.co.il",
                AutodeskCredentialPurpose: AutodeskCredentialPurpose.AccServiceAdmin));

        Assert.False(decision.Allowed);
        Assert.Contains("siad@si-eng.co.il", decision.Reason ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("danny@si-eng.co.il", decision.Reason ?? "", StringComparison.OrdinalIgnoreCase);
        // Operator coherence itself remains Match — denial is AccService Admin identity, not SIUser.
        Assert.Equal(IdentityCoherenceStatus.Match, decision.Snapshot.Status);
    }

    [Fact]
    public void Status_bar_transitions_pending_to_match_text()
    {
        var pending = IdentityStatusDisplay.FormatFooter(new IdentityCoherenceSnapshot(
            IdentityCoherenceStatus.PendingApproval, 1, "X", "login", null,
            false, null, null, null, null, null, AccAuthMode.ApplicationTwoLegged,
            null, null, null, null, null));
        Assert.Contains("ממתין", pending, StringComparison.Ordinal);

        var match = IdentityStatusDisplay.FormatFooter(new IdentityCoherenceSnapshot(
            IdentityCoherenceStatus.Match, 1, "Danny", "login", "danny@si.co.il",
            true, "danny@si.co.il", true, true, true, true, AccAuthMode.ApplicationTwoLegged,
            null, null, null, null, null));
        Assert.Contains("תקינה", match, StringComparison.Ordinal);

        var mismatch = IdentityStatusDisplay.FormatFooter(new IdentityCoherenceSnapshot(
            IdentityCoherenceStatus.Mismatch, 1, "Danny", "login", "danny@si.co.il",
            false, "wrong@si.co.il", false, false, false, false, AccAuthMode.ApplicationTwoLegged,
            null, null, null, null, "mismatch"));
        Assert.Contains("אי התאמת", mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Arbitrary_login_name_with_correct_email_can_match()
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            42, "Odd", "weird-login-format!!", AppRole.Employee, true,
            Email: "correct@si.co.il"));
        var auth = new StubConnectorAuth { IsAuthenticated = true, ConnectedAccountEmail = "correct@si.co.il" };
        var coherence = new IdentityCoherenceService(session, new NoOpRefresh(session), auth);

        var snap = await coherence.EvaluateAsync(new IdentityCoherenceEvaluateOptions(DisconnectGoogleOnMismatch: false));

        Assert.Equal(IdentityCoherenceStatus.Match, snap.Status);
        Assert.NotEqual("weird-login-format!!", snap.GoogleEmail);
    }

    [Fact]
    public async Task Guard_denies_before_external_side_effect_on_pending()
    {
        var session = new AuthenticatedUserSession();
        session.SetAuthenticated(new CurrentUserProfileDto(
            1, "P", "login", AppRole.Unauthorized, true));
        var guard = new IdentityOperationGuard(new IdentityCoherenceService(session, new NoOpRefresh(session)));
        var externalCalled = false;

        var decision = await guard.EvaluateAsync(IdentityOperationKind.GmailWrite);
        if (decision.Allowed)
        {
            externalCalled = true;
        }

        Assert.False(decision.Allowed);
        Assert.False(externalCalled);
    }

    private sealed class NoOpRefresh(AuthenticatedUserSession session) : ICurrentUserSessionRefreshService
    {
        public Task<CurrentUserProfileDto?> RefreshCurrentUserAsync(CancellationToken cancellationToken = default)
            => session.GetCurrentUserAsync(cancellationToken);
    }

    private sealed class StubAccProbe : IAccHumanMembershipProbe
    {
        public AccHumanMembershipProbeResult? Result { get; set; }

        public Task<AccHumanMembershipProbeResult?> ProbeAsync(
            string? accProjectId,
            string expectedEmail,
            bool allowReconcile = true,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result);

        public Task<AccHumanMembershipProbeResult?> ProbeForSiProjectAsync(
            int siProjectId,
            string expectedEmail,
            bool allowReconcile = true,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
    }

    private sealed class StubConnectorAuth : IConnectorAuthService
    {
        public bool IsAuthenticated { get; set; }
        public string? ConnectedAccountEmail { get; set; }
        public bool LogoutCalled { get; private set; }
        public event Action<bool>? AuthStateChanged;

        public Task<bool> LoginAsync(ConnectorLoginOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public void Logout()
        {
            LogoutCalled = true;
            IsAuthenticated = false;
            ConnectedAccountEmail = null;
            AuthStateChanged?.Invoke(false);
        }

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            Logout();
            return Task.CompletedTask;
        }

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(IsAuthenticated);

        public Task RefreshAccountProfileAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

public sealed class SqlWindowsPendingRegistrationTests
{
    [Fact]
    public async Task Unknown_login_creates_exactly_one_pending_siuser()
    {
        await using var provider = BuildProvider(out var factory);
        var authenticator = provider.GetRequiredService<SqlWindowsCurrentUserAuthenticator>();
        authenticator.RuntimeLoginResolver = () => @"TESTDOMAIN\pending_user_xyz";

        var result = await authenticator.AuthenticateAsync();

        Assert.Equal(WindowsUserAuthStatus.PendingApproval, result.Status);
        Assert.NotNull(result.Profile);
        Assert.Equal(AppRole.Unauthorized, result.Profile!.Role);
        Assert.True(result.Profile.IsActive);
        Assert.Null(result.Profile.Email);
        Assert.Equal(AppAccUserType.NoAccUser, result.Profile.AccUserType);

        await using var db = await factory.CreateDbContextAsync();
        var count = await db.Users.CountAsync(u => u.LoginName == @"TESTDOMAIN\pending_user_xyz");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Concurrent_startup_does_not_duplicate_pending_user()
    {
        await using var provider = BuildProvider(out var factory);
        var login = @"TESTDOMAIN\concurrent_" + Guid.NewGuid().ToString("N");

        async Task<WindowsUserAuthenticationResult> RunOnce()
        {
            var session = new AuthenticatedUserSession();
            var auth = new SqlWindowsCurrentUserAuthenticator(factory, session, new NullLogger());
            auth.RuntimeLoginResolver = () => login;
            return await auth.AuthenticateAsync();
        }

        var results = await Task.WhenAll(RunOnce(), RunOnce(), RunOnce(), RunOnce());

        Assert.All(results, r => Assert.Equal(WindowsUserAuthStatus.PendingApproval, r.Status));
        await using var db = await factory.CreateDbContextAsync();
        var count = await db.Users.CountAsync(u => u.LoginName == login);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Inactive_siuser_is_blocked_and_not_reactivated()
    {
        await using var provider = BuildProvider(out var factory);
        var login = @"TESTDOMAIN\inactive_user";
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Users.Add(new SiUserEntity
            {
                LoginName = login,
                Name = "Inactive",
                IsActive = false,
                Role = (int)AppRole.Employee,
                Email = "x@y.com",
            });
            await db.SaveChangesAsync();
        }

        var authenticator = provider.GetRequiredService<SqlWindowsCurrentUserAuthenticator>();
        authenticator.RuntimeLoginResolver = () => login;

        var result = await authenticator.AuthenticateAsync();

        Assert.Equal(WindowsUserAuthStatus.Blocked, result.Status);
        await using var db2 = await factory.CreateDbContextAsync();
        var user = await db2.Users.SingleAsync(u => u.LoginName == login);
        Assert.False(user.IsActive);
    }

    [Fact]
    public async Task Admin_approval_refresh_sees_updated_role_and_email()
    {
        await using var provider = BuildProvider(out var factory);
        var login = @"TESTDOMAIN\approve_me";
        var session = provider.GetRequiredService<AuthenticatedUserSession>();
        var authenticator = provider.GetRequiredService<SqlWindowsCurrentUserAuthenticator>();
        authenticator.RuntimeLoginResolver = () => login;
        var created = await authenticator.AuthenticateAsync();
        Assert.Equal(WindowsUserAuthStatus.PendingApproval, created.Status);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var user = await db.Users.SingleAsync(u => u.LoginName == login);
            user.Role = (int)AppRole.Employee;
            user.Email = "approved@si.co.il";
            await db.SaveChangesAsync();
        }

        var refresh = provider.GetRequiredService<ICurrentUserSessionRefreshService>();
        var updated = await refresh.RefreshCurrentUserAsync();

        Assert.NotNull(updated);
        Assert.Equal(AppRole.Employee, updated!.Role);
        Assert.Equal("approved@si.co.il", updated.Email);
        Assert.True(session.HasAccess);
    }

    private static ServiceProvider BuildProvider(out IDbContextFactory<SiNetDbContext> factory)
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContextFactory<SiNetDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<AuthenticatedUserSession>();
        services.AddSingleton<ICurrentUserContext>(sp => sp.GetRequiredService<AuthenticatedUserSession>());
        services.AddSingleton<ICurrentUserProfileService>(sp => sp.GetRequiredService<AuthenticatedUserSession>());
        services.AddSingleton<IAppLogger, NullLogger>();
        services.AddTransient<SqlWindowsCurrentUserAuthenticator>();
        services.AddTransient<ICurrentUserSessionRefreshService, SqlCurrentUserSessionRefreshService>();
        var provider = services.BuildServiceProvider();
        factory = provider.GetRequiredService<IDbContextFactory<SiNetDbContext>>();
        return provider;
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
