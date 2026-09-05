using SiNet.Application.Identity;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Resolves AccService Admin expected email (SystemSettings) vs connected Autodesk profile.
/// Used to fail-closed Admin API ops when a known wrong Autodesk account is connected.
/// </summary>
public interface IAccServiceAdminIdentityProbe
{
    Task<AccServiceAdminIdentityCheck> EvaluateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Settings-only probe: when connected profile email is supplied via options/context elsewhere,
/// callers can use <see cref="AccServiceAdminIdentity.Evaluate"/>. This probe returns expected
/// from SystemSettings and leaves connected unknown unless an optional connected email is passed.
/// </summary>
public sealed class SystemSettingsAccServiceAdminIdentityProbe(
    ISystemSettingsQueryService settings) : IAccServiceAdminIdentityProbe
{
    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));

    public async Task<AccServiceAdminIdentityCheck> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        return AccServiceAdminIdentity.Evaluate(dto.Acc.AccServiceExpectedAdminEmail, connectedProfileEmail: null);
    }

    public AccServiceAdminIdentityCheck EvaluateWithConnected(string? connectedProfileEmail, SystemSettingsDto settingsDto) =>
        AccServiceAdminIdentity.Evaluate(settingsDto.Acc.AccServiceExpectedAdminEmail, connectedProfileEmail);
}
