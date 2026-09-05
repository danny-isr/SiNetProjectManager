using SiNet.Application.Identity;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Sql.Services.Identity;

public interface IAccServiceAdminIdentityProbe
{
    Task<AccServiceAdminIdentityCheck> EvaluateAsync(CancellationToken cancellationToken = default);

    AccServiceAdminIdentityCheck EvaluateWithConnected(string? connectedProfileEmail, SystemSettingsDto settingsDto);
}

/// <summary>
/// Resolves expected AccService Admin from <see cref="SystemSettingKeys.AccBootstrapAdminEmail"/>.
/// Callers that know the connected Autodesk profile use <see cref="EvaluateWithConnected"/>.
/// </summary>
public sealed class SystemSettingsAccServiceAdminIdentityProbe(
    ISystemSettingsQueryService settings) : IAccServiceAdminIdentityProbe
{
    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));

    public async Task<AccServiceAdminIdentityCheck> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        return AccServiceAdminIdentity.Evaluate(
            dto.Acc.AccBootstrapAdminEmail,
            actualAdminEmail: null,
            tokenAvailable: true,
            profileResolved: false);
    }

    public AccServiceAdminIdentityCheck EvaluateWithConnected(
        string? connectedProfileEmail,
        SystemSettingsDto settingsDto)
    {
        ArgumentNullException.ThrowIfNull(settingsDto);
        return AccServiceAdminIdentity.Evaluate(
            settingsDto.Acc.AccBootstrapAdminEmail,
            connectedProfileEmail,
            tokenAvailable: true,
            profileResolved: !string.IsNullOrWhiteSpace(connectedProfileEmail));
    }
}
