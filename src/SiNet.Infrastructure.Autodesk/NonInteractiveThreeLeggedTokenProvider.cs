using MyOffice.AutodeskConnector;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>
/// AccService / server-side decorator: 3-legged refresh is allowed; interactive
/// browser OAuth (HttpListener / localhost callback) is never opened.
/// Interactive AccServiceAdmin login belongs only in <c>SiOffice.AccService.AuthOnce</c>.
/// </summary>
public sealed class NonInteractiveThreeLeggedTokenProvider : ITokenProvider
{
    private readonly ITokenProvider _inner;

    public NonInteractiveThreeLeggedTokenProvider(ITokenProvider inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task<string> GetTwoLeggedTokenAsync(CancellationToken ct = default) =>
        _inner.GetTwoLeggedTokenAsync(ct);

    public async Task<string> GetThreeLeggedAdminTokenAsync(CancellationToken ct = default)
    {
        using (TokenProvider.SuppressInteractiveBrowserAuthScope())
        {
            return await _inner.GetThreeLeggedAdminTokenAsync(ct).ConfigureAwait(false);
        }
    }

    public bool HasThreeLeggedRefreshToken => _inner.HasThreeLeggedRefreshToken;

    public string ClientId => _inner.ClientId;

    public string ThreeLeggedRefreshTokenStoragePath => _inner.ThreeLeggedRefreshTokenStoragePath;

    public AutodeskTokenStorePurpose TokenStorePurpose => _inner.TokenStorePurpose;
}
