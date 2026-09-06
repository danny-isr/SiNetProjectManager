using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.Identity;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// The one place the L4W tier is allowed to create data that is not part of the scenario itself.
/// Everything here must be a lookup row whose absence would block the run outright — see
/// <c>docs/TEST_STRATEGY.md</c> §4W.2.2. Behavioural data (users, workflow definitions,
/// project-type mappings) is never seeded; its absence is reported as Blocked.
/// </summary>
internal static class PilotSmokeSeed
{
    /// <summary>
    /// Ensures a <c>Place</c> titled <c>SI</c> exists, because the ACC project name is derived as
    /// <c>"SI-" + Place.Title</c> and that derivation is what keeps project filing inside a
    /// disposable ACC project. Idempotent: a restored database loses the row, a re-run recreates it.
    /// </summary>
    public static async Task<(int PlaceId, bool Created)> EnsureSiPlaceAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int actingUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.Places
            .AsNoTracking()
            .Where(p => p.Title == PilotSmokeEnvironment.RequiredAccPlaceTitle)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != 0)
        {
            return (existing, false);
        }

        var now = DateTime.Now;
        var place = new Place
        {
            Title = PilotSmokeEnvironment.RequiredAccPlaceTitle,
            InUse = true,
            Created = now,
            Modified = now,
            AuthorId = actingUserId,
            EditorId = actingUserId,
        };

        db.Places.Add(place);
        await db.SaveChangesAsync(cancellationToken);
        return (place.Id, true);
    }

    internal sealed record OperatorLogin(
        int UserId,
        string WindowsLogin,
        string? PreviousLoginName,
        bool Changed);

    /// <summary>
    /// Ensures the declared operator <c>SIUser</c> is the row the current Windows identity resolves
    /// to. A database restored from the production server carries that server's <c>LoginName</c>, so
    /// nothing on this workstation authenticates until the row is repointed.
    /// <para>
    /// Deliberately narrow: it never creates a user and never touches group memberships or roles,
    /// because those decide who workflow tasks are assigned to. If the Windows identity already
    /// resolves to a <em>different</em> user it refuses, rather than quietly moving the login.
    /// </para>
    /// </summary>
    public static async Task<OperatorLogin> EnsureOperatorLoginAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int declaredOperatorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbFactory);

        var windowsLogin = System.Security.Principal.WindowsIdentity.GetCurrent().Name;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var matching = await db.Siusers
            .AsNoTracking()
            .Where(u => u.IsActive && u.LoginName == windowsLogin)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (matching == declaredOperatorUserId)
        {
            return new OperatorLogin(declaredOperatorUserId, windowsLogin, null, Changed: false);
        }

        if (matching != 0)
        {
            throw new InvalidOperationException(
                $"Windows identity '{windowsLogin}' already resolves to SIUser {matching}, but "
                + $"{PilotSmokeEnvironment.OperatorUserIdEnv} declares {declaredOperatorUserId}. "
                + "Refusing to move a login between users. Either declare the resolved id or fix the "
                + "row deliberately.");
        }

        var target = await db.Siusers
            .FirstOrDefaultAsync(u => u.Id == declaredOperatorUserId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"SIUser {declaredOperatorUserId} does not exist, so its login cannot be repointed.");

        if (!target.IsActive)
        {
            throw new InvalidOperationException(
                $"SIUser {declaredOperatorUserId} is inactive. Activating a user is a permission "
                + "decision, not a test fixture — reporting Blocked instead.");
        }

        var previous = target.LoginName;
        target.LoginName = windowsLogin;
        await db.SaveChangesAsync(cancellationToken);

        return new OperatorLogin(declaredOperatorUserId, windowsLogin, previous, Changed: true);
    }

    /// <summary>
    /// Binds <see cref="AuthenticatedUserSession"/> the same way the production host does after
    /// Windows login resolution — via <see cref="IWindowsCurrentUserAuthenticator"/>.
    /// <para>
    /// LoginName alignment (<see cref="EnsureOperatorLoginAsync"/>) is necessary but not sufficient:
    /// <c>IdentityOperationGuard</c> / <c>WorkflowMutate</c> reads the in-process session through
    /// <see cref="IIdentityCoherenceService"/>. Without this bind, StartAsync fails with
    /// <c>IdentityOperationDeniedException</c> before <c>IPilotStartGate</c> runs.
    /// </para>
    /// Does not weaken production guards and does not register PilotSmoke-specific authorization.
    /// </summary>
    public static async Task<WindowsUserAuthenticationResult> EnsureAuthorizedOperatorSessionAsync(
        IServiceProvider services,
        int declaredOperatorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var authenticator = services.GetRequiredService<IWindowsCurrentUserAuthenticator>();
        var result = await authenticator.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

        if (result.Status is not WindowsUserAuthStatus.Authorized)
        {
            throw new InvalidOperationException(
                "PilotSmoke could not bind an Authorized SIUser session for WorkflowMutate. "
                + $"Status={result.Status}, reason='{result.FailureReason ?? "<none>"}'.");
        }

        if (result.Profile is null || result.Profile.UserId != declaredOperatorUserId)
        {
            throw new InvalidOperationException(
                "Authenticated session UserId does not match "
                + $"{PilotSmokeEnvironment.OperatorUserIdEnv}={declaredOperatorUserId}. "
                + $"Resolved UserId={result.Profile?.UserId.ToString() ?? "<null>"}.");
        }

        // Defense in depth: coherence must see the same profile the host would use.
        var session = services.GetRequiredService<AuthenticatedUserSession>();
        if (!session.HasSession || session.UserId != declaredOperatorUserId)
        {
            throw new InvalidOperationException(
                "AuthenticatedUserSession was not populated after AuthenticateAsync.");
        }

        return result;
    }
}
