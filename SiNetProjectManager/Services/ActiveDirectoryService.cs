using System.DirectoryServices.AccountManagement;
using System.Runtime.InteropServices;
using SiNetSQL.Services;

namespace SiNetProjectManager.Services;

/// <summary>
/// Queries Active Directory for domain user accounts.
/// Supports both domain-joined machines (auto-detect) and
/// non-domain-joined machines connected via VPN (configured domain name).
/// </summary>
public static class ActiveDirectoryService
{
    /// <summary>
    /// Lightweight DTO representing an AD user for selection purposes.
    /// </summary>
    public sealed record AdUserInfo(
        string DisplayName,
        string SamAccountName,
        string? Email,
        string DomainLoginName);

    /// <summary>
    /// Queries the domain for enabled user accounts.
    /// Uses <see cref="AppConfiguration.AdDomainName"/> when set (VPN scenario),
    /// otherwise auto-detects via machine domain membership.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of AD users sorted by display name.</returns>
    public static Task<List<AdUserInfo>> GetDomainUsersAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var users = new List<AdUserInfo>();
            var configuredDomain = AppConfiguration.AdDomainName;

            try
            {
                using var context = CreatePrincipalContext(configuredDomain);

                // Validate credentials early — PrincipalContext validates lazily,
                // so without this the COMException surfaces deep inside PrincipalSearcher.
                ValidateContextConnection(context, configuredDomain);

                using var searcher = new PrincipalSearcher(new UserPrincipal(context)
                {
                    Enabled = true
                });

                // Use the configured domain name or fall back to env domain
                var domainPrefix = configuredDomain ?? Environment.UserDomainName;

                // Strip FQDN to NetBIOS for DOMAIN\user format if needed
                if (domainPrefix.Contains('.'))
                    domainPrefix = domainPrefix.Split('.')[0];

                foreach (var result in searcher.FindAll())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (result is UserPrincipal user &&
                        !string.IsNullOrWhiteSpace(user.SamAccountName))
                    {
                        var displayName = !string.IsNullOrWhiteSpace(user.DisplayName)
                            ? user.DisplayName
                            : user.SamAccountName;

                        // Prefer the mail attribute; fall back to UserPrincipalName (often user@domain format)
                        var email = user.EmailAddress;
                        if (string.IsNullOrWhiteSpace(email) &&
                            !string.IsNullOrWhiteSpace(user.UserPrincipalName) &&
                            user.UserPrincipalName.Contains('@'))
                        {
                            email = user.UserPrincipalName;
                        }

                        users.Add(new AdUserInfo(
                            DisplayName: displayName,
                            SamAccountName: user.SamAccountName,
                            Email: email,
                            DomainLoginName: $@"{domainPrefix}\{user.SamAccountName}"));
                    }
                }

                AppLogger.Info($"[ActiveDirectoryService] Loaded {users.Count} users from domain " +
                               $"(configured={configuredDomain ?? "auto-detect"})");
            }
            catch (PrincipalServerDownException ex)
            {
                var hint = configuredDomain == null
                    ? "המחשב לא מחובר לדומיין. הגדר את ActiveDirectory:DomainName ב-appsettings.json (לדוגמה: si-eng.local)"
                    : $"לא ניתן להתחבר לדומיין '{configuredDomain}'. ודא שה-VPN פעיל ושהשם נכון.";
                AppLogger.Error(ex, $"[ActiveDirectoryService] Domain controller unreachable. {hint}");
                throw new InvalidOperationException(hint, ex);
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x8007052E))
            {
                // 0x8007052E = ERROR_LOGON_FAILURE — DC was reached but credentials rejected
                var vaultUser = CredentialVaultService.GetSecret(SecretKeys.AdUsername);
                var hint = string.IsNullOrEmpty(vaultUser)
                    ? "לא הוגדרו פרטי התחברות ל-Active Directory.\n" +
                      "פתח 'הגדרת מפתחות ואישורים' (תפריט מערכת) והזן שם משתמש וסיסמה של משתמש בדומיין."
                    : $"פרטי ההתחברות של '{vaultUser}' נדחו על ידי הדומיין '{configuredDomain ?? "auto"}'.\n" +
                      "ודא שהשם והסיסמה נכונים בהגדרות (תפריט מערכת → הגדרת מפתחות ואישורים).\n" +
                      "פורמט נדרש: DOMAIN\\user או user@domain.com";
                AppLogger.Error(ex, $"[ActiveDirectoryService] Authentication failed. VaultUser={vaultUser ?? "(none)"}");
                throw new InvalidOperationException(hint, ex);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "[ActiveDirectoryService] Failed to query domain users");
                throw;
            }

            return users
                .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Creates a PrincipalContext — auto-detect for domain-joined machines,
    /// explicit domain name for VPN/workgroup machines.
    /// When AD credentials are stored in the vault, uses them for authentication.
    /// Uses SimpleBind for non-domain-joined machines where Negotiate/Kerberos is unavailable.
    /// </summary>
    private static PrincipalContext CreatePrincipalContext(string? domainName)
    {
        var vaultUsername = CredentialVaultService.GetSecret(SecretKeys.AdUsername);
        var vaultPassword = CredentialVaultService.GetSecret(SecretKeys.AdPassword);
        var hasCredentials = !string.IsNullOrEmpty(vaultUsername) && !string.IsNullOrEmpty(vaultPassword);

        if (!string.IsNullOrEmpty(domainName))
        {
            if (hasCredentials)
            {
                AppLogger.Info($"[ActiveDirectoryService] Connecting to domain: {domainName} with vault credentials ({vaultUsername})");
                // SimpleBind: direct LDAP bind — required for non-domain-joined machines (AzureAD/WORKGROUP)
                // where Negotiate/Kerberos fails because the machine has no domain trust relationship.
                return new PrincipalContext(ContextType.Domain, domainName, null,
                    ContextOptions.SimpleBind, vaultUsername, vaultPassword);
            }

            AppLogger.Info($"[ActiveDirectoryService] Connecting to configured domain: {domainName}");
            return new PrincipalContext(ContextType.Domain, domainName);
        }

        if (hasCredentials)
        {
            AppLogger.Info("[ActiveDirectoryService] Auto-detecting domain with vault credentials");
            return new PrincipalContext(ContextType.Domain, null, null,
                ContextOptions.SimpleBind, vaultUsername, vaultPassword);
        }

        AppLogger.Info("[ActiveDirectoryService] Auto-detecting domain (machine is domain-joined)");
        return new PrincipalContext(ContextType.Domain);
    }

    /// <summary>
    /// Eagerly validates the PrincipalContext connection so auth errors surface with clear messages
    /// instead of cryptic COMExceptions deep inside PrincipalSearcher.
    /// </summary>
    private static void ValidateContextConnection(PrincipalContext context, string? configuredDomain)
    {
        var vaultUsername = CredentialVaultService.GetSecret(SecretKeys.AdUsername);
        var vaultPassword = CredentialVaultService.GetSecret(SecretKeys.AdPassword);

        if (!string.IsNullOrEmpty(vaultUsername) && !string.IsNullOrEmpty(vaultPassword))
        {
            // Must use SimpleBind here too — Negotiate fails on non-domain-joined machines
            if (!context.ValidateCredentials(vaultUsername, vaultPassword, ContextOptions.SimpleBind))
            {
                throw new COMException(
                    "The user name or password is incorrect.",
                    unchecked((int)0x8007052E));
            }

            AppLogger.Info($"[ActiveDirectoryService] Credentials validated for {vaultUsername}");
        }
        else if (!string.IsNullOrEmpty(configuredDomain))
        {
            AppLogger.Info("[ActiveDirectoryService] No vault credentials — relying on current session");
        }
    }
}
