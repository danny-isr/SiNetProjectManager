namespace SiNet.Application.DevTools;

/// <summary>
/// DEBUG-only development database reset. New System port — not the legacy SiNetSQL static service.
/// </summary>
public interface IDevDataResetService
{
    string CurrentWindowsUser { get; }
    bool IsCurrentUserAllowed();
    ValueTask<string?> PeekDatabaseNameAsync(CancellationToken ct = default);
    ValueTask<DevDataResetResult> ResetAsync(DevDataResetOptions options, CancellationToken ct = default);
}
