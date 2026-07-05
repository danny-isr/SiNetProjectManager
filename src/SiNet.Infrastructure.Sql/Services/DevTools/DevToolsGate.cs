using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>
/// Authorization gate for destructive DEBUG dev-tools. Mirrors legacy FullAccess + Windows user checks.
/// </summary>
public sealed class DevToolsGate(IAuthorizationQueryService? authorization = null)
{
    private readonly IAuthorizationQueryService? _authorization = authorization;

    public void EnsureDevToolsAuthorized(string operationName)
    {
#if !DEBUG
        throw new NotSupportedException($"{operationName} is available in DEBUG builds only.");
#else
        if (!DevToolsWindowsUserPolicy.IsCurrentUserAllowed())
        {
            throw new UnauthorizedAccessException(
                $"{operationName} is restricted. Current user '{DevToolsWindowsUserPolicy.CurrentWindowsUser}' is not allowed.");
        }

        if (_authorization is null)
        {
            throw new UnauthorizedAccessException(
                $"{operationName} requires an authenticated host user with Management access.");
        }

        var canReset = _authorization
            .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.DevToolsReset)
            .GetAwaiter()
            .GetResult();

        if (!canReset)
        {
            throw new UnauthorizedAccessException(
                $"{operationName} requires Management role (DevTools.Reset feature).");
        }
#endif
    }
}
