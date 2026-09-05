using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Identity;
using SiNet.Application.Runtime;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Permanent System Health row: AccService Admin expected vs actual token identity.
/// Prefers AccService <c>/v1/acc/admin-identity</c>; falls back to local token + settings on DEV.
/// </summary>
public sealed class AccAdminIdentityStatusContributor(
    ISystemSettingsQueryService settings,
    IAccServiceAdminIdentityRemoteProbe? remoteProbe = null,
    ITokenProvider? tokenProvider = null) : ISubsystemStatusContributor
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(12);

    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));

    public string Key => "acc-admin-identity";

    public string DisplayNameHe => "ACC Admin Identity";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        AccServiceAdminIdentityCheck check;
        try
        {
            check = await ResolveAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Row(SubsystemRuntimeState.Degraded, "ACC Admin: תם הזמן בבדיקת זהות");
        }
        catch (Exception ex)
        {
            return Row(SubsystemRuntimeState.Degraded, $"ACC Admin: בדיקה נכשלה — {ex.GetType().Name}");
        }

        return Classify(check);
    }

    private async Task<AccServiceAdminIdentityCheck> ResolveAsync(CancellationToken cancellationToken)
    {
        if (remoteProbe is not null)
        {
            var remote = await remoteProbe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (remote.Reachable)
            {
                // Prefer server-evaluated fields; rebuild via Evaluate for AdminApi layering consistency.
                return AccServiceAdminIdentity.WithAdminApiStatus(
                    AccServiceAdminIdentity.Evaluate(
                        remote.ExpectedAdminEmail,
                        remote.ActualAdminEmail,
                        remote.TokenAvailable,
                        remote.ProfileResolved,
                        remote.AutodeskUserId,
                        remote.DisplayName),
                    remote.AdminApiStatus);
            }
        }

        var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (tokenProvider is null)
        {
            return AccServiceAdminIdentity.Evaluate(
                dto.Acc.AccBootstrapAdminEmail,
                actualAdminEmail: null,
                tokenAvailable: false,
                profileResolved: false);
        }

        var profile = await AccServiceAdminTokenProfile.ResolveAsync(tokenProvider, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return AccServiceAdminIdentity.Evaluate(
            dto.Acc.AccBootstrapAdminEmail,
            profile.Email,
            profile.TokenAvailable,
            profile.ProfileResolved,
            profile.AutodeskUserId,
            profile.DisplayName);
    }

    private static SubsystemRuntimeStatus Classify(AccServiceAdminIdentityCheck check)
    {
        var expected = check.ExpectedAdminEmail;
        var actual = check.ActualAdminEmail ?? "(לא זמין)";
        var userId = string.IsNullOrWhiteSpace(check.AutodeskUserId) ? "" : $" | UserId: {check.AutodeskUserId}";
        var adminApi = string.IsNullOrWhiteSpace(check.AdminApiStatus)
            ? ""
            : $" | Admin API: {check.AdminApiStatus}";

        return check.Status switch
        {
            AccServiceAdminIdentityStatus.Healthy =>
                Row(SubsystemRuntimeState.Idle, $"ACC Admin: תקין — {expected}{adminApi}"),

            AccServiceAdminIdentityStatus.AdminEmailMismatch =>
                Row(
                    SubsystemRuntimeState.Degraded,
                    $"ACC Admin: אי התאמה{Environment.NewLine}מוגדר: {expected}{Environment.NewLine}מחובר: {actual}"),

            AccServiceAdminIdentityStatus.AdminApiUnauthorized =>
                Row(
                    SubsystemRuntimeState.Degraded,
                    "ACC Admin: החשבון נכון אך חסרות הרשאות Account Admin"),

            AccServiceAdminIdentityStatus.TokenMissing =>
                Row(SubsystemRuntimeState.Degraded, $"ACC Admin: חסר טוקן — מצופה {expected}"),

            AccServiceAdminIdentityStatus.ProfileUnavailable =>
                Row(SubsystemRuntimeState.Degraded, $"ACC Admin: פרופיל לא זמין — מצופה {expected}"),

            _ =>
                Row(
                    SubsystemRuntimeState.Degraded,
                    $"ACC Admin: לא זמין — מצופה {expected} | מחובר: {actual}{userId}{adminApi}"),
        };
    }

    private static SubsystemRuntimeStatus Row(SubsystemRuntimeState state, string summary) =>
        new("acc-admin-identity", "ACC Admin Identity", state, null, summary, DateTimeOffset.UtcNow);
}
