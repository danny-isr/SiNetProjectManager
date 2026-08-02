namespace SiNet.Application.Identity;

/// <summary>
/// DEBUG / manual-test helper: temporarily mutate the current Windows user's
/// <c>SIUser</c> role/active flags. Not a production authentication mechanism.
/// Hosts must call this only under <c>#if DEBUG</c> and when
/// <c>EnableAuthorizationTestMode</c> is enabled.
/// </summary>
public interface IDebugAuthorizationRoleOverrideService
{
    ValueTask<DebugAuthorizationCurrentUserDto> GetCurrentUserAsync(
        CancellationToken cancellationToken = default);

    ValueTask ApplyChoiceAsync(
        DebugAuthorizationRoleChoice choice,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RestoreOriginalAsync(CancellationToken cancellationToken = default);
}

public enum DebugAuthorizationRoleChoice
{
    NoChange = 0,
    Administrator = 1,
    Management = 2,
    Employee = 3,
    Unauthorized = 4,
    Inactive = 5,
}

public sealed record DebugAuthorizationCurrentUserDto(
    string WindowsLogin,
    string? DisplayName,
    AppRole? Role,
    bool? IsActive,
    bool UserFound,
    bool HasBackup);
