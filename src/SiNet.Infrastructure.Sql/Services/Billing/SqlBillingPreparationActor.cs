using SiNet.Application.Billing;
using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.Billing;

public sealed class SqlBillingPreparationActor(
    ICurrentUserContext currentUser,
    ICurrentUserProfileService? profile = null) : IBillingPreparationActor
{
    private readonly ICurrentUserContext _currentUser =
        currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    private readonly ICurrentUserProfileService? _profile = profile;

    public async Task<BillingActor> GetAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("נדרש משתמש מחובר לבקשת הכנת חשבון.");
        var dto = _profile is null
            ? null
            : await _profile.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        var login = !string.IsNullOrWhiteSpace(dto?.LoginName)
            ? dto.LoginName
            : !string.IsNullOrWhiteSpace(dto?.DisplayName)
                ? dto.DisplayName
                : $"משתמש #{userId}";
        return new BillingActor(userId, login);
    }
}
