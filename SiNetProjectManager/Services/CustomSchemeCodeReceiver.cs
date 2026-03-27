using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Serilog;
using System.Diagnostics;

namespace SiNetProjectManager.Services;

/// <summary>
/// Custom <see cref="ICodeReceiver"/> that uses a local HTTP loopback listener
/// for OAuth 2.0 redirect. Opens the system browser for Google consent and receives
/// the authorization code via HTTP callback on 127.0.0.1.
///
/// This follows RFC 8252 §7.3 (Loopback Interface Redirect) — the recommended
/// approach for native desktop applications. No custom URI scheme registration required.
///
/// Flow:
/// 1. <see cref="RedirectUri"/> getter starts a local HTTP listener on a random port
/// 2. <see cref="ReceiveCodeAsync"/> opens the system browser to the Google consent page
/// 3. User authenticates and grants consent in the system browser
/// 4. Google redirects to <c>http://127.0.0.1:{port}/oauth2callback?code=AUTH_CODE</c>
/// 5. Listener captures the code and returns it to the Google client library
/// 6. A success page is displayed in the browser; the listener is disposed
/// </summary>
internal sealed class CustomSchemeCodeReceiver : ICodeReceiver
{
    private OAuthLoopbackListener? _activeListener;

    /// <inheritdoc />
    /// <remarks>
    /// The redirect URI is dynamic (changes per auth attempt due to random port).
    /// The Google client library reads this BEFORE calling <see cref="ReceiveCodeAsync"/>,
    /// so the listener is started eagerly on first access to ensure the port is reserved.
    /// </remarks>
    public string RedirectUri
    {
        get
        {
            _activeListener ??= new OAuthLoopbackListener();
            return _activeListener.RedirectUri;
        }
    }

    /// <inheritdoc />
    public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(
        AuthorizationCodeRequestUrl url,
        CancellationToken taskCancellationToken)
    {
        // Ensure the listener is running (may already be started via RedirectUri getter)
        _activeListener ??= new OAuthLoopbackListener();

        try
        {
            var authorizationUrl = url.Build().ToString();
            Log.Information("Opening system browser for Google OAuth consent. RedirectUri={RedirectUri}",
                _activeListener.RedirectUri);

            // Open system browser — the user authenticates there
            Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true });

            // Wait for the HTTP callback from the browser redirect
            var callbackUri = await _activeListener.WaitForCallbackAsync(taskCancellationToken);

            // Parse the callback query parameters (code, error, scope, etc.)
            var queryParams = ParseCallbackQuery(callbackUri);

            var response = new AuthorizationCodeResponseUrl();

            if (queryParams.TryGetValue("code", out var code))
                response.Code = code;
            if (queryParams.TryGetValue("error", out var error))
                response.Error = error;
            if (queryParams.TryGetValue("error_description", out var errorDesc))
                response.ErrorDescription = errorDesc;
            if (queryParams.TryGetValue("error_uri", out var errorUri))
                response.ErrorUri = errorUri;

            Log.Information("OAuth callback processed. HasCode={HasCode}, HasError={HasError}",
                !string.IsNullOrEmpty(response.Code),
                !string.IsNullOrEmpty(response.Error));

            return response;
        }
        finally
        {
            _activeListener?.Dispose();
            _activeListener = null;
        }
    }

    /// <summary>
    /// Parses the query string from a callback URI into a dictionary.
    /// </summary>
    private static Dictionary<string, string> ParseCallbackQuery(string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(uri)) return result;

        try
        {
            var parsed = new Uri(uri);
            var query = parsed.Query.TrimStart('?');
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                {
                    result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse OAuth callback URI: {Uri}", uri);
        }

        return result;
    }
}
