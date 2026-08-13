using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Logging;
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
/// Fast: TLS/reachability + local key presence. Deep: authenticated <c>/v1/acc/diag</c> (401).
/// </summary>
public sealed class AccServiceStatusContributor(
    IAccServiceHealthProbe probe,
    IAccServiceKeyDiagnostics? keyDiagnostics = null,
    IAccServiceDiagnosticsProbe? diagnosticsProbe = null,
    IAppLogger? logger = null) : ISubsystemStatusContributor
{
    private readonly IAccServiceHealthProbe _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private readonly IAccServiceKeyDiagnostics? _keyDiagnostics = keyDiagnostics;
    private readonly IAccServiceDiagnosticsProbe? _diagnosticsProbe = diagnosticsProbe;
    private readonly IAppLogger? _logger = logger;

    public string Key => "acc-service";

    public string DisplayNameHe => "SiOffice.AccService (פנימי)";

    public Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default)
        => ContributeAsync(new SubsystemProbeContext(IncludeDeep: false), cancellationToken);

    public async Task<SubsystemRuntimeStatus> ContributeAsync(
        SubsystemProbeContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await _probe.CheckAsync(cancellationToken).ConfigureAwait(false);
        var keyInfo = _keyDiagnostics?.Describe();
        var hasLocalKey = keyInfo?.HasApiKey == true;

        AccServiceDiagnosticsResult? diag = null;
        if (context.IncludeDeep && _diagnosticsProbe is not null && hasLocalKey)
        {
            diag = await _diagnosticsProbe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }

        var (state, summary) = Classify(result, keyInfo, diag, context.IncludeDeep);
        LogIfUnhealthy(state, summary, result, diag);
        return new SubsystemRuntimeStatus(Key, DisplayNameHe, state, null, summary, DateTimeOffset.UtcNow);
    }

    private static (SubsystemRuntimeState State, string Summary) Classify(
        AccServiceHealthResult result,
        AccServiceKeyInfo? keyInfo,
        AccServiceDiagnosticsResult? diag,
        bool includeDeep)
    {
        if (result.State == AccServiceHealthState.NotConfigured)
        {
            return (SubsystemRuntimeState.NotConfigured, "כתובת השירות לא הוגדרה");
        }

        if (result.State != AccServiceHealthState.Online)
        {
            var detail = string.IsNullOrWhiteSpace(result.Detail)
                ? result.Endpoint
                : result.Detail;
            return (SubsystemRuntimeState.Degraded, $"לא זמין — {detail}");
        }

        if (keyInfo is { HasApiKey: false })
        {
            return (SubsystemRuntimeState.Degraded, $"זמין — חסר מפתח AccService ב-Vault — {result.Endpoint}");
        }

        if (includeDeep && diag is not null)
        {
            if (LooksLikeUnauthorized(diag))
            {
                return (SubsystemRuntimeState.Degraded, "זמין — HTTP 401, המפתח נדחה");
            }

            if (!diag.Reachable)
            {
                var diagDetail = diag.AutodeskDetail ?? diag.DbDetail ?? "diag נכשל";
                return (SubsystemRuntimeState.Degraded, $"זמין — diag: {diagDetail}");
            }
        }

        return (SubsystemRuntimeState.Idle, $"זמין — {result.Endpoint}");
    }

    private static bool LooksLikeUnauthorized(AccServiceDiagnosticsResult diag) =>
        (diag.AutodeskDetail ?? string.Empty).Contains("401", StringComparison.Ordinal)
        || (diag.DbDetail ?? string.Empty).Contains("401", StringComparison.Ordinal);

    private void LogIfUnhealthy(
        SubsystemRuntimeState state,
        string summary,
        AccServiceHealthResult result,
        AccServiceDiagnosticsResult? diag)
    {
        if (state is SubsystemRuntimeState.Idle or SubsystemRuntimeState.Running)
        {
            return;
        }

        _logger?.Warn(
            $"[AccService] Health classified as {state} on {Environment.MachineName}/{Environment.UserName} " +
            $"category={ClassifyLogCategory(summary, result, diag)} http={ExtractHttp(summary, result, diag)} " +
            $"exceptionType={ExtractExceptionType(result)}");
    }

    private static string ClassifyLogCategory(
        string summary,
        AccServiceHealthResult result,
        AccServiceDiagnosticsResult? diag)
    {
        if ((diag is not null && LooksLikeUnauthorized(diag))
            || summary.Contains("401", StringComparison.Ordinal)
            || summary.Contains("חסר מפתח", StringComparison.Ordinal))
        {
            return "ApiKeyRejected";
        }

        if (summary.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("TLS", StringComparison.OrdinalIgnoreCase)
            || (result.Detail ?? string.Empty).Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || (result.Detail ?? string.Empty).Contains("AuthenticationException", StringComparison.OrdinalIgnoreCase))
        {
            return "Tls";
        }

        return result.State.ToString();
    }

    private static string ExtractHttp(
        string summary,
        AccServiceHealthResult result,
        AccServiceDiagnosticsResult? diag)
    {
        if (summary.Contains("401", StringComparison.Ordinal)
            || (diag is not null && LooksLikeUnauthorized(diag)))
        {
            return "401";
        }

        var detail = result.Detail ?? string.Empty;
        if (detail.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase))
            return detail["HTTP ".Length..].Trim();
        return "-";
    }

    private static string ExtractExceptionType(AccServiceHealthResult result)
    {
        var detail = result.Detail ?? string.Empty;
        var colon = detail.IndexOf(':');
        return colon > 0 ? detail[..colon] : detail;
    }
}
