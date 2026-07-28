using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Secrets;

/// <summary>
/// Native New System AD user search. Uses <see cref="IDirectoryUserConnectionProvider"/> — not legacy MVVM.
/// Shared by standalone App.Wpf and the V2 New System graph.
/// </summary>
public sealed class ActiveDirectoryUserLookupService(
    IDirectoryUserConnectionProvider connectionProvider,
    IAppLogger? logger = null) : IDirectoryUserLookupService
{
    private const int MaxResults = 100;
    private readonly IDirectoryUserConnectionProvider _connectionProvider =
        connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
    private readonly IAppLogger? _logger = logger;

    private IReadOnlyList<DirectoryUserDto>? _cachedUsers;

    public bool IsConfigured
    {
        get
        {
            var settings = _connectionProvider.GetConnectionSettings();
            return !string.IsNullOrWhiteSpace(settings.DomainName)
                   || settings.HasCredentials
                   || !string.IsNullOrWhiteSpace(Environment.UserDomainName);
        }
    }

    public async Task<IReadOnlyList<DirectoryUserDto>> SearchUsersAsync(
        string searchText,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Array.Empty<DirectoryUserDto>();
        }

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return Array.Empty<DirectoryUserDto>();
        }

        var allUsers = _cachedUsers ??= await LoadDomainUsersAsync(cancellationToken).ConfigureAwait(false);
        var term = searchText.Trim();

        return allUsers
            .Where(u => MatchesSearch(u, term))
            .Take(MaxResults)
            .ToList();
    }

    private static bool MatchesSearch(DirectoryUserDto user, string term)
    {
        return Contains(user.DisplayName, term)
               || Contains(user.LoginName, term)
               || Contains(user.Email, term);
    }

    private static bool Contains(string? value, string term)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private Task<IReadOnlyList<DirectoryUserDto>> LoadDomainUsersAsync(CancellationToken cancellationToken)
        => Task.Run(() => LoadDomainUsers(cancellationToken), cancellationToken);

    private IReadOnlyList<DirectoryUserDto> LoadDomainUsers(CancellationToken cancellationToken)
    {
        var settings = _connectionProvider.GetConnectionSettings();
        var configuredDomain = settings.DomainName;
        var users = new List<DirectoryUserDto>();

        try
        {
            using var context = CreatePrincipalContext(settings);

            ValidateContextConnection(context, settings);

            using var searcher = new PrincipalSearcher(new UserPrincipal(context)
            {
                Enabled = true,
            });

            var domainPrefix = configuredDomain ?? Environment.UserDomainName;
            if (!string.IsNullOrWhiteSpace(domainPrefix) && domainPrefix.Contains('.'))
            {
                domainPrefix = domainPrefix.Split('.')[0];
            }

            foreach (var result in searcher.FindAll())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (result is not UserPrincipal user || string.IsNullOrWhiteSpace(user.SamAccountName))
                {
                    continue;
                }

                var displayName = !string.IsNullOrWhiteSpace(user.DisplayName)
                    ? user.DisplayName
                    : user.SamAccountName;

                var email = user.EmailAddress;
                if (string.IsNullOrWhiteSpace(email)
                    && !string.IsNullOrWhiteSpace(user.UserPrincipalName)
                    && user.UserPrincipalName.Contains('@'))
                {
                    email = user.UserPrincipalName;
                }

                users.Add(new DirectoryUserDto(
                    LoginName: $@"{domainPrefix}\{user.SamAccountName}",
                    DisplayName: displayName,
                    Email: email));
            }

            _logger?.Info(
                $"[ActiveDirectoryUserLookupService] Loaded {users.Count} users from domain " +
                $"(configured={configuredDomain ?? "auto-detect"})");
        }
        catch (PrincipalServerDownException ex)
        {
            var hint = configuredDomain == null
                ? "המחשב לא מחובר לדומיין. הגדר ActiveDirectory:DomainName ב-appsettings.json."
                : $"לא ניתן להתחבר לדומיין '{configuredDomain}'. ודא שה-VPN פעיל ושהשם נכון.";
            _logger?.Error($"[ActiveDirectoryUserLookupService] Domain controller unreachable. {hint}", ex);
            throw new InvalidOperationException(hint, ex);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x8007052E))
        {
            var vaultUser = settings.Username;
            var hint = string.IsNullOrEmpty(vaultUser)
                ? "לא הוגדרו פרטי התחברות ל-Active Directory. פתח 'הגדרת מפתחות ואישורים' והזן משתמש בדומיין."
                : $"פרטי ההתחברות של '{vaultUser}' נדחו על ידי הדומיין. ודא שהשם והסיסמה נכונים.";
            _logger?.Error(
                $"[ActiveDirectoryUserLookupService] Authentication failed. VaultUser={vaultUser ?? "(none)"}",
                ex);
            throw new InvalidOperationException(hint, ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Error("[ActiveDirectoryUserLookupService] Failed to query domain users", ex);
            throw;
        }

        return users
            .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PrincipalContext CreatePrincipalContext(DirectoryUserConnectionSettings settings)
    {
        var domainName = settings.DomainName;
        var hasCredentials = settings.HasCredentials;

        if (!string.IsNullOrEmpty(domainName))
        {
            if (hasCredentials)
            {
                return new PrincipalContext(
                    ContextType.Domain,
                    domainName,
                    null,
                    ContextOptions.SimpleBind,
                    settings.Username!,
                    settings.Password!);
            }

            return new PrincipalContext(ContextType.Domain, domainName);
        }

        if (hasCredentials)
        {
            return new PrincipalContext(
                ContextType.Domain,
                null,
                null,
                ContextOptions.SimpleBind,
                settings.Username!,
                settings.Password!);
        }

        return new PrincipalContext(ContextType.Domain);
    }

    private static void ValidateContextConnection(PrincipalContext context, DirectoryUserConnectionSettings settings)
    {
        if (!settings.HasCredentials)
        {
            return;
        }

        if (!context.ValidateCredentials(settings.Username!, settings.Password!, ContextOptions.SimpleBind))
        {
            throw new COMException(
                "The user name or password is incorrect.",
                unchecked((int)0x8007052E));
        }
    }
}
