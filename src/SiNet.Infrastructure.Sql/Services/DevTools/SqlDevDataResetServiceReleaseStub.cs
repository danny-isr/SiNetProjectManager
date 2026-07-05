using SiNet.Application.DevTools;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

#if !DEBUG
/// <summary>Release stub — destructive dev reset is DEBUG-only.</summary>
public sealed class SqlDevDataResetServiceReleaseStub : IDevDataResetService
{
    public string CurrentWindowsUser => DevToolsWindowsUserPolicy.CurrentWindowsUser;
    public bool IsCurrentUserAllowed() => false;

    public ValueTask<string?> PeekDatabaseNameAsync(CancellationToken ct = default) =>
        ValueTask.FromResult<string?>(null);

    public ValueTask<DevDataResetResult> ResetAsync(DevDataResetOptions options, CancellationToken ct = default) =>
        throw new NotSupportedException("Dev data reset is available in DEBUG builds only.");
}
#endif
