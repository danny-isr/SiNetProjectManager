using System.Net.Http;
using System.Text;
using SiNet.Application.Ai;

namespace SiNet.Infrastructure.Sql.Services.Ai;

/// <summary>Default HTTP seam. Tests replace this; WPF must not call it.</summary>
internal sealed class HttpClientAiHttpTransport : IAiHttpTransport, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<AiHttpResponse> SendAsync(AiHttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Uri);
        if (!string.IsNullOrEmpty(request.JsonBody))
        {
            message.Content = new StringContent(request.JsonBody, Encoding.UTF8, "application/json");
        }

        CancellationTokenSource? timeoutCts = null;
        if (request.Timeout is { } timeout)
        {
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
        }

        using (timeoutCts)
        {
        var token = timeoutCts?.Token ?? cancellationToken;

        try
        {
            using var response = await _http.SendAsync(message, token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return new AiHttpResponse((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("AI HTTP request timed out.");
        }
        }
    }

    public void Dispose() => _http.Dispose();
}
