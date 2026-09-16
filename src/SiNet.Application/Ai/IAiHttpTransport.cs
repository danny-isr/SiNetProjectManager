namespace SiNet.Application.Ai;

/// <summary>Testable HTTP seam for AI providers. WPF must not call this.</summary>
public interface IAiHttpTransport
{
    Task<AiHttpResponse> SendAsync(AiHttpRequest request, CancellationToken cancellationToken = default);
}

public sealed record AiHttpRequest(
    string Method,
    Uri Uri,
    string? JsonBody,
    TimeSpan? Timeout);

public sealed record AiHttpResponse(int StatusCode, string Body);
