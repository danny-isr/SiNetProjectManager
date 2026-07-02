namespace SiNet.Application.Identity;

/// <summary>
/// Supplies Active Directory connection settings to <see cref="IDirectoryUserLookupService"/>.
/// Host binds this to vault/appsettings — Infrastructure never hardcodes domain credentials.
/// </summary>
public interface IDirectoryUserConnectionProvider
{
    DirectoryUserConnectionSettings GetConnectionSettings();
}

/// <summary>AD domain and optional vault credentials for VPN / workgroup machines.</summary>
public sealed class DirectoryUserConnectionSettings
{
    /// <summary>Configured domain name (e.g. si-eng.local). Null/empty enables auto-detect on domain-joined machines.</summary>
    public string? DomainName { get; init; }

    /// <summary>Optional service account for LDAP SimpleBind (DOMAIN\user or user@domain).</summary>
    public string? Username { get; init; }

    /// <summary>Password for <see cref="Username"/>.</summary>
    public string? Password { get; init; }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>Default provider when the host has not bound AD settings yet.</summary>
public sealed class NullDirectoryUserConnectionProvider : IDirectoryUserConnectionProvider
{
    public static NullDirectoryUserConnectionProvider Instance { get; } = new();

    public DirectoryUserConnectionSettings GetConnectionSettings() => new();
}

/// <summary>Returns an empty result set when AD is not configured.</summary>
public sealed class NullDirectoryUserLookupService : IDirectoryUserLookupService
{
    public static NullDirectoryUserLookupService Instance { get; } = new();

    public bool IsConfigured => false;

    public Task<IReadOnlyList<DirectoryUserDto>> SearchUsersAsync(
        string searchText,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DirectoryUserDto>>(Array.Empty<DirectoryUserDto>());
}
