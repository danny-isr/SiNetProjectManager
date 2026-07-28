using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Diagnostics;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class ModeSwitchingAccInboxBootstrapService(
    IAccServiceModeProvider serviceModeProvider,
    IAccInboxBootstrapLocalExecutor? localAccInboxBootstrapExecutor,
    RemoteAccInboxBootstrapService remoteAccInboxBootstrapService) : IAccInboxBootstrapService
{
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly IAccInboxBootstrapLocalExecutor? _localAccInboxBootstrapExecutor = localAccInboxBootstrapExecutor;
    private readonly RemoteAccInboxBootstrapService _remoteAccInboxBootstrapService = remoteAccInboxBootstrapService;

    public Task<AccInboxBootstrapResult> EnsureAsync(CancellationToken cancellationToken = default)
    {
        // #region agent log
        AgentDebugNdjson.Write(
            "H1",
            "ModeSwitchingAccInboxBootstrapService.EnsureAsync",
            "bootstrap mode selected",
            new Dictionary<string, object?>
            {
                ["mode"] = _serviceModeProvider.Mode.ToString(),
                ["hasBaseUrl"] = !string.IsNullOrWhiteSpace(_serviceModeProvider.BaseUrl),
                ["baseUrlHost"] = TryHost(_serviceModeProvider.BaseUrl),
                ["hasLocalExecutor"] = _localAccInboxBootstrapExecutor is not null,
            });
        // #endregion

        if (_serviceModeProvider.Mode == AccServiceMode.Remote)
        {
            return _remoteAccInboxBootstrapService.EnsureAsync(cancellationToken);
        }

        if (_localAccInboxBootstrapExecutor is null)
        {
            // #region agent log
            AgentDebugNdjson.Write(
                "H1",
                "ModeSwitchingAccInboxBootstrapService.EnsureAsync",
                "local bootstrap unavailable",
                new Dictionary<string, object?> { ["mode"] = "Local" });
            // #endregion
            throw new InvalidOperationException(
                "ACC Inbox bootstrap in Local mode is not available without a local executor " +
                "(standalone New System host). Configure AccService Remote (BaseUrl) or use the V2 host.");
        }

        return _localAccInboxBootstrapExecutor.EnsureAsync(cancellationToken);
    }

    private static string? TryHost(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : "(unparsed)";
    }
}
