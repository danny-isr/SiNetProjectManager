using SiNet.Application.Abstractions.Autodesk;

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
        if (_serviceModeProvider.Mode == AccServiceMode.Remote)
        {
            return _remoteAccInboxBootstrapService.EnsureAsync(cancellationToken);
        }

        if (_localAccInboxBootstrapExecutor is null)
        {
            throw new InvalidOperationException(
                "ACC Inbox bootstrap in Local mode is not available without a local executor. " +
                "Register IAccInboxBootstrapLocalExecutor (StandaloneNew / V2 host) or configure AccService Remote (BaseUrl).");
        }

        return _localAccInboxBootstrapExecutor.EnsureAsync(cancellationToken);
    }
}
