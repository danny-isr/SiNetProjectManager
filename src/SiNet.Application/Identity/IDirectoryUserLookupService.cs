namespace SiNet.Application.Identity;

/// <summary>
/// Native Active Directory user search port for the New System add-user dialog.
/// Host binds a Windows implementation; Infrastructure defaults to <see cref="NullDirectoryUserLookupService"/>.
/// </summary>
public interface IDirectoryUserLookupService
{
    /// <summary>False when AD is not configured (no domain / credentials on the host).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Searches enabled domain users by display name, login, or email (case-insensitive contains).
    /// Returns an empty list when <see cref="IsConfigured"/> is false.
    /// </summary>
    Task<IReadOnlyList<DirectoryUserDto>> SearchUsersAsync(
        string searchText,
        CancellationToken cancellationToken = default);
}
