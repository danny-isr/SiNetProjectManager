namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Loads/saves Gmail mailbox view preferences for the current user from <c>UserSetting</c>.
/// Missing rows / empty values resolve to <see cref="UserMailViewPreferences.Default"/>.
/// </summary>
public interface IUserMailViewPreferencesService
{
    Task<UserMailViewPreferences> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(UserMailViewPreferences preferences, CancellationToken cancellationToken = default);
}
