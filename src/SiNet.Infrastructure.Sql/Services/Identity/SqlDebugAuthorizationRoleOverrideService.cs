using System.Security.Principal;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;
using SiNet.Infrastructure.Sql.Entities;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// DEBUG helper: mutates <c>SIUser</c> for the current Windows login and keeps a local
/// restore snapshot under <c>%LocalAppData%\SiNet\debug_original_role.json</c>.
/// </summary>
public sealed class SqlDebugAuthorizationRoleOverrideService : IDebugAuthorizationRoleOverrideService
{
    private static readonly string BackupFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SiNet",
        "debug_original_role.json");

    private readonly IDbContextFactory<SiNetDbContext> _dbFactory;

    public SqlDebugAuthorizationRoleOverrideService(IDbContextFactory<SiNetDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async ValueTask<DebugAuthorizationCurrentUserDto> GetCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        var windowsLogin = WindowsIdentity.GetCurrent().Name;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var user = await FindUserAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return new DebugAuthorizationCurrentUserDto(
                windowsLogin,
                DisplayName: null,
                Role: null,
                IsActive: null,
                UserFound: false,
                HasBackup: HasMatchingBackup(windowsLogin));
        }

        EnsureBackup(user);

        return new DebugAuthorizationCurrentUserDto(
            windowsLogin,
            DisplayName: user.Name,
            Role: (AppRole)user.Role,
            IsActive: user.IsActive,
            UserFound: true,
            HasBackup: HasMatchingBackup(windowsLogin));
    }

    public async ValueTask ApplyChoiceAsync(
        DebugAuthorizationRoleChoice choice,
        CancellationToken cancellationToken = default)
    {
        if (choice == DebugAuthorizationRoleChoice.NoChange)
            return;

        var windowsLogin = WindowsIdentity.GetCurrent().Name;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var user = await FindUserAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"SIUser row not found for Windows login '{windowsLogin}'.");

        EnsureBackup(user);

        switch (choice)
        {
            case DebugAuthorizationRoleChoice.Administrator:
                user.Role = (int)AppRole.Administrator;
                user.IsActive = true;
                break;
            case DebugAuthorizationRoleChoice.Management:
                user.Role = (int)AppRole.Management;
                user.IsActive = true;
                break;
            case DebugAuthorizationRoleChoice.Employee:
                user.Role = (int)AppRole.Employee;
                user.IsActive = true;
                break;
            case DebugAuthorizationRoleChoice.Unauthorized:
                user.Role = (int)AppRole.Unauthorized;
                user.IsActive = true;
                break;
            case DebugAuthorizationRoleChoice.Inactive:
                user.Role = (int)AppRole.Employee;
                user.IsActive = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(choice), choice, "Unknown debug role choice.");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> RestoreOriginalAsync(CancellationToken cancellationToken = default)
    {
        var windowsLogin = WindowsIdentity.GetCurrent().Name;
        if (!TryReadBackup(out var state)
            || !string.Equals(state.LoginName, windowsLogin, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var user = await FindUserAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false);
        if (user is null)
            return false;

        user.Role = (int)state.Role;
        user.IsActive = state.IsActive;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (File.Exists(BackupFilePath))
                File.Delete(BackupFilePath);
        }
        catch
        {
            // Best-effort cleanup; restore already succeeded.
        }

        return true;
    }

    private static async Task<SiUserEntity?> FindUserAsync(
        SiNetDbContext db,
        string windowsLogin,
        CancellationToken cancellationToken)
    {
        var loginLower = windowsLogin.ToLowerInvariant();
        var user = await db.Users
            .FirstOrDefaultAsync(
                u => u.LoginName != null && u.LoginName.ToLower() == loginLower,
                cancellationToken)
            .ConfigureAwait(false);

        if (user is not null)
            return user;

        var slash = windowsLogin.LastIndexOf('\\');
        if (slash < 0 || slash >= windowsLogin.Length - 1)
            return null;

        var suffix = "\\" + windowsLogin[(slash + 1)..].ToLowerInvariant();
        return await db.Users
            .FirstOrDefaultAsync(
                u => u.LoginName != null && u.LoginName.ToLower().EndsWith(suffix),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureBackup(SiUserEntity user)
    {
        try
        {
            var dir = Path.GetDirectoryName(BackupFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(BackupFilePath))
                return;

            var state = new OriginalUserState
            {
                LoginName = user.LoginName,
                Role = (AppRole)user.Role,
                IsActive = user.IsActive,
            };
            File.WriteAllText(BackupFilePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to backup original user state to '{BackupFilePath}'.", ex);
        }
    }

    private static bool HasMatchingBackup(string windowsLogin)
        => TryReadBackup(out var state)
           && string.Equals(state.LoginName, windowsLogin, StringComparison.OrdinalIgnoreCase);

    private static bool TryReadBackup(out OriginalUserState state)
    {
        state = default!;
        try
        {
            if (!File.Exists(BackupFilePath))
                return false;

            var json = File.ReadAllText(BackupFilePath);
            var parsed = JsonSerializer.Deserialize<OriginalUserState>(json);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.LoginName))
                return false;

            state = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class OriginalUserState
    {
        public string? LoginName { get; set; }
        public AppRole Role { get; set; }
        public bool IsActive { get; set; }
    }
}
