using Serilog;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Listens on a local HTTP port for the Google OAuth 2.0 redirect callback.
/// Uses the loopback interface (127.0.0.1) with a randomly assigned free port
/// per RFC 8252 §7.3 (Loopback Interface Redirect).
///
/// Lifecycle:
/// 1. Constructor picks a free TCP port and starts the <see cref="HttpListener"/>
/// 2. <see cref="WaitForCallbackAsync"/> blocks until the browser redirect arrives
/// 3. A success page is sent to the browser, and the callback URI is returned
/// 4. <see cref="Dispose"/> stops the listener
///
/// This replaces the named-pipe approach (<c>OAuthCallbackPipe</c>) with a standard
/// HTTP loopback listener — no custom URI scheme registration required.
/// </summary>
internal sealed class OAuthLoopbackListener : IDisposable
{
    private const string CallbackPath = "/oauth2callback";
    private readonly HttpListener _listener;
    private bool _disposed;

    /// <summary>The TCP port selected for this listener instance.</summary>
    internal int Port { get; }

    /// <summary>
    /// The full redirect URI including the callback path.
    /// This value is used as <c>redirect_uri</c> in the OAuth authorization request.
    /// </summary>
    internal string RedirectUri { get; }

    internal OAuthLoopbackListener()
    {
        Port = GetAvailablePort();
        RedirectUri = $"http://127.0.0.1:{Port}{CallbackPath}/";

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();

        Log.Information("OAuth loopback listener started on port {Port}", Port);
    }

    /// <summary>
    /// Waits for a single HTTP request from the browser (the OAuth redirect)
    /// and returns the full request URI including query parameters.
    /// Sends a friendly success page back to the browser before returning.
    /// </summary>
    internal async Task<string> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Stop the listener when cancellation is requested
            using var registration = cancellationToken.Register(() => _listener.Stop());

            var context = await _listener.GetContextAsync();
            var requestUri = context.Request.Url?.ToString() ?? string.Empty;

            Log.Information("OAuth loopback callback received ({Bytes} bytes query)",
                context.Request.Url?.Query?.Length ?? 0);

            // Send a friendly response so the browser shows a success page
            var responseHtml = GetSuccessHtml();
            var responseBytes = Encoding.UTF8.GetBytes(responseHtml);

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(
                responseBytes.AsMemory(0, responseBytes.Length), cancellationToken);
            context.Response.Close();

            return requestUri;
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    /// <summary>Finds a free TCP port on the loopback interface.</summary>
    private static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static string GetSuccessHtml() =>
        """
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><title>Authentication Complete</title></head>
        <body style="font-family:'Segoe UI',sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:#f0f9f0;color:#2e7d32;">
            <div style="text-align:center;">
                <div style="font-size:64px;">&#10004;</div>
                <h2>Authentication Successful</h2>
                <p>You can close this window and return to the application.</p>
            </div>
        </body>
        </html>
        """;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
