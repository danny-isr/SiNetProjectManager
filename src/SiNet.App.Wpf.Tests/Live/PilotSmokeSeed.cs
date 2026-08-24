using Microsoft.EntityFrameworkCore;
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
}
