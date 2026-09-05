using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Identity;
using SiNet.Application.Runtime;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Permanent System Health row: AccService Admin expected vs actual token identity.
/// Prefers AccService <c>/v1/acc/admin-identity</c>; falls back to local token + settings on DEV.
/// Healthy requires a real Admin API probe == 200.
/// </summary>
public sealed class AccAdminIdentityStatusContributor(
    ISystemSettingsQueryService settings,
    IAccServiceAdminIdentityRemoteProbe? remoteProbe = null,
    ITokenProvider? tokenProvider = null,
    IAccServiceAdminApiStatusProbe? adminApiProbe = null) : ISubsystemStatusContributor
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    private readonly ISystemSettingsQueryService _settings =
        settings ?? throw new ArgumentNullException(nameof(settings));

    private string? _lastTokenStoragePath;
    private string? _lastTokenPurpose;
    private bool _wrongStore;

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
        _wrongStore = false;
        _lastTokenPurpose = null;
        _lastTokenStoragePath = null;

        if (remoteProbe is not null)
        {
            var remote = await remoteProbe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (remote.Reachable)
            {
                _lastTokenStoragePath = remote.TokenStoragePath;
                _lastTokenPurpose = remote.TokenPurpose;
                if (!IsDedicatedAccServiceStore(remote.TokenPurpose, remote.TokenStoragePath))
                {
                    _wrongStore = true;
                }

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
            _lastTokenStoragePath = AutodeskTokenStorePaths.GetDefaultRefreshTokenFilePath(
                AutodeskTokenStorePurpose.AccServiceAdmin);
            _lastTokenPurpose = AutodeskTokenStorePurpose.AccServiceAdmin.ToString();
            return AccServiceAdminIdentity.WithAdminApiStatus(
                AccServiceAdminIdentity.Evaluate(
                    dto.Acc.AccBootstrapAdminEmail,
                    actualAdminEmail: null,
                    tokenAvailable: false,
                    profileResolved: false),
                "unavailable");
        }

        _lastTokenStoragePath = tokenProvider.ThreeLeggedRefreshTokenStoragePath;
        _lastTokenPurpose = tokenProvider.TokenStorePurpose.ToString();
        if (!IsDedicatedAccServiceStore(_lastTokenPurpose, _lastTokenStoragePath))
        {
            _wrongStore = true;
        }

        var profile = await AccServiceAdminTokenProfile.ResolveAsync(tokenProvider, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var identity = AccServiceAdminIdentity.Evaluate(
            dto.Acc.AccBootstrapAdminEmail,
            profile.Email,
            profile.TokenAvailable,
            profile.ProfileResolved,
            profile.AutodeskUserId,
            profile.DisplayName);

        string? adminApiStatus = "unavailable";
        if (!_wrongStore
            && identity.EmailMatch
            && identity.ProfileResolved
            && identity.TokenAvailable
            && adminApiProbe is not null)
        {
            try
            {
                adminApiStatus = await adminApiProbe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                adminApiStatus = $"unavailable:{ex.GetType().Name}";
            }
        }

        return AccServiceAdminIdentity.WithAdminApiStatus(identity, adminApiStatus);
    }

    private SubsystemRuntimeStatus Classify(AccServiceAdminIdentityCheck check)
    {
        var expected = check.ExpectedAdminEmail;
        var actual = check.ActualAdminEmail ?? "(לא זמין)";

        if (_wrongStore)
        {
            return Row(
                SubsystemRuntimeState.Degraded,
                "ACC Admin — מחסן טוקן שגוי" + Environment.NewLine +
                $"מחסן: {_lastTokenStoragePath ?? "(לא ידוע)"}" + Environment.NewLine +
                $"Purpose: {_lastTokenPurpose ?? "(לא ידוע)"}");
        }

        return check.Status switch
        {
            AccServiceAdminIdentityStatus.Healthy =>
                Row(
                    SubsystemRuntimeState.Idle,
                    "ACC Admin" + Environment.NewLine +
                    $"חשבון מוגדר: {expected}" + Environment.NewLine +
                    $"חשבון מחובר: {actual}" + Environment.NewLine +
                    "מחסן: AccService" + Environment.NewLine +
                    "זהות: תקינה" + Environment.NewLine +
                    FormatAdminApiLine(check.AdminApiStatus)),

            AccServiceAdminIdentityStatus.AdminEmailMismatch =>
                Row(
                    SubsystemRuntimeState.Degraded,
                    "ACC Admin — שגיאת זהות" + Environment.NewLine +
                    $"מוגדר: {expected}" + Environment.NewLine +
                    $"מחובר: {actual}"),

            AccServiceAdminIdentityStatus.AdminApiUnauthorized =>
                Row(
                    SubsystemRuntimeState.Degraded,
                    "ACC Admin — החשבון נכון, אך חסרות הרשאות Account Admin"),

            AccServiceAdminIdentityStatus.TokenMissing =>
                Row(
                    SubsystemRuntimeState.Degraded,
                    $"ACC Admin: חסר טוקן שירות — מצופה {expected}" + Environment.NewLine +
                    "מחסן: AccService"),

            AccServiceAdminIdentityStatus.ProfileUnavailable =>
                Row(
                    SubsystemRuntimeState.Degraded,
                    $"ACC Admin: פרופיל לא זמין — מצופה {expected}"),

            AccServiceAdminIdentityStatus.ServiceUnavailable =>
                Row(
                    SubsystemRuntimeState.Degraded,
                    "ACC Admin — זהות תקינה, אך Admin API לא זמין" + Environment.NewLine +
                    $"Admin API: {check.AdminApiStatus ?? "unavailable"}"),

            _ =>
                Row(
                    SubsystemRuntimeState.Degraded,
                    $"ACC Admin: לא זמין — מצופה {expected} | מחובר: {actual}"),
        };
    }

    private static string FormatAdminApiLine(string? adminApiStatus)
    {
        if (string.IsNullOrWhiteSpace(adminApiStatus))
        {
            return "Admin API: (לא נבדק)";
        }

        if (string.Equals(adminApiStatus, "OK", StringComparison.OrdinalIgnoreCase)
            || string.Equals(adminApiStatus, "200", StringComparison.OrdinalIgnoreCase)
            || (adminApiStatus.Length > 0 && adminApiStatus[0] == '2'))
        {
            return "Admin API: תקין";
        }

        return $"Admin API: {adminApiStatus}";
    }

    internal static bool IsDedicatedAccServiceStore(string? tokenPurpose, string? tokenStoragePath)
    {
        if (!string.IsNullOrWhiteSpace(tokenPurpose)
            && !string.Equals(tokenPurpose, AutodeskTokenStorePurpose.AccServiceAdmin.ToString(), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tokenPurpose, AccServiceTokenPackageMeta.TokenPurposeValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(tokenStoragePath))
        {
            return string.IsNullOrWhiteSpace(tokenPurpose)
                || string.Equals(tokenPurpose, AutodeskTokenStorePurpose.AccServiceAdmin.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        return AccServiceTokenPackageMeta.IsDedicatedAccServiceTokenPath(tokenStoragePath);
    }

    private static SubsystemRuntimeStatus Row(SubsystemRuntimeState state, string summary) =>
        new("acc-admin-identity", "ACC Admin Identity", state, null, summary, DateTimeOffset.UtcNow);
}
