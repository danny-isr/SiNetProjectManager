using SiNet.Application.Identity;
using SiNetSQL.Services.Authorization;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host adapter: maps legacy <see cref="IActionPermissionService"/> to the clean
/// <see cref="IActionPermissionQueryService"/> port. WPF and NewShell must not reference
/// <see cref="IActionPermissionService"/> or EF types directly.
/// </summary>
internal sealed class LegacyActionPermissionQueryService : IActionPermissionQueryService
{
    private readonly IActionPermissionService _actionPermissionService;
    private readonly ICurrentUserContext _currentUserContext;

    public LegacyActionPermissionQueryService(
        IActionPermissionService actionPermissionService,
        ICurrentUserContext currentUserContext)
    {
        _actionPermissionService = actionPermissionService
            ?? throw new ArgumentNullException(nameof(actionPermissionService));
        _currentUserContext = currentUserContext
            ?? throw new ArgumentNullException(nameof(currentUserContext));
    }

    /// <inheritdoc />
    public Task<bool> CanUserExecuteActionAsync(
        string actionCode,
        int userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _actionPermissionService.IsUserAllowedForActionAsync(actionCode, userId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanCurrentUserExecuteActionAsync(
        string actionCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_currentUserContext.UserId is not int userId)
        {
            return Task.FromResult(false);
        }

        return CanUserExecuteActionAsync(actionCode, userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRefDto>> GetAuthorizedUsersForActionAsync(
        string actionCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var users = await _actionPermissionService
            .GetAuthorizedUsersForActionAsync(actionCode, cancellationToken)
            .ConfigureAwait(false);

        return users
            .Select(u => new UserRefDto(
                UserId: u.Id,
                DisplayName: u.Name ?? string.Empty,
                LoginName: u.LoginName))
            .ToList();
    }
}
