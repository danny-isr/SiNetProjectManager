using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Runtime;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// Autodesk token reachability, ported from the legacy <c>AutodeskAccHealthCheck</c>. Only the
/// 2-legged (client_credentials) token is requested — asking for a 3-legged token could open a
/// browser, which a status probe must never do.
/// </summary>
public sealed class AutodeskTokenStatusContributor(ITokenProvider tokenProvider) : ISubsystemStatusContributor
{
    private static readonly TimeSpan TokenTimeout = TimeSpan.FromSeconds(7);

    private readonly ITokenProvider _tokenProvider =
        tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));

    public string Key => "autodesk-acc";

    public string DisplayNameHe => "Autodesk ACC";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_tokenProvider.ClientId))
            return Row(SubsystemRuntimeState.NotConfigured, "Client ID לא הוגדר");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TokenTimeout);

        string token;
        try
        {
            token = await _tokenProvider.GetTwoLeggedTokenAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Row(SubsystemRuntimeState.Degraded, "תם הזמן בהמתנה לטוקן");
        }
        catch (HttpRequestException ex)
        {
            return Row(SubsystemRuntimeState.Degraded, $"קבלת טוקן נכשלה: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(token))
            return Row(SubsystemRuntimeState.Degraded, "התקבל טוקן ריק");

        // ACC Admin endpoints need a 3-legged token; without one only file/data operations work.
        return _tokenProvider.HasThreeLeggedRefreshToken
            ? Row(SubsystemRuntimeState.Idle, "טוקן תקין (2-legged + 3-legged)")
            : Row(SubsystemRuntimeState.Degraded, "2-legged בלבד — פעולות Admin ידרשו התחברות");
    }

    private SubsystemRuntimeStatus Row(SubsystemRuntimeState state, string summary) =>
        new(Key, DisplayNameHe, state, null, summary, DateTimeOffset.UtcNow);
}

/// <summary>
/// AccService endpoint health, ported from the legacy <c>InternalAccServiceHealthCheck</c>. Unlike
/// the built-in <c>acc</c> row, which reports "מצב מקומי" without probing in Local mode, this row
/// always probes so a misconfigured endpoint is visible in both modes.
/// </summary>
public sealed class AccServiceStatusContributor(IAccServiceHealthProbe probe) : ISubsystemStatusContributor
{
    private readonly IAccServiceHealthProbe _probe = probe ?? throw new ArgumentNullException(nameof(probe));

    public string Key => "acc-service";

    public string DisplayNameHe => "SiOffice.AccService (פנימי)";

    public async Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _probe.CheckAsync(cancellationToken).ConfigureAwait(false);

        var state = result.State switch
        {
            AccServiceHealthState.Online => SubsystemRuntimeState.Idle,
            AccServiceHealthState.NotConfigured => SubsystemRuntimeState.NotConfigured,
            _ => SubsystemRuntimeState.Degraded,
        };

        var summary = result.State switch
        {
            AccServiceHealthState.Online => $"זמין — {result.Endpoint}",
            AccServiceHealthState.NotConfigured => "כתובת השירות לא הוגדרה",
            _ => string.IsNullOrWhiteSpace(result.Detail)
                ? $"לא זמין — {result.Endpoint}"
                : $"לא זמין — {result.Detail}",
        };

        return new SubsystemRuntimeStatus(Key, DisplayNameHe, state, null, summary, DateTimeOffset.UtcNow);
    }
}
