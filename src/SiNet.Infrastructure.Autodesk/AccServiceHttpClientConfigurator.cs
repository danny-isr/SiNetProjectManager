using System.Net.Http;

namespace SiNet.Infrastructure.Autodesk;

internal static class AccServiceHttpClientConfigurator
{
    public static void ConfigureFileTransferClient(HttpClient client, AccServiceControlPlaneOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        client.Timeout = options.FileTransferTimeout;
    }

    public static HttpMessageHandler CreateHandler(AccServiceControlPlaneOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, _, _, errors) =>
            {
                if (errors == System.Net.Security.SslPolicyErrors.None)
                {
                    return true;
                }

                if (errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors)
                {
                    var requestUri = message?.RequestUri;
                    if (requestUri?.IsLoopback == true)
                    {
                        return true;
                    }

                    return IsApprovedInternalHost(requestUri?.Host, options);
                }

                return false;
            },
        };
    }

    private static bool IsApprovedInternalHost(string? host, AccServiceControlPlaneOptions options)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (options.ApprovedSelfSignedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (options.ApprovedSelfSignedHostSuffixes.Any(suffix =>
                host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return options.ApprovedSelfSignedIpPrefixes.Any(prefix =>
            host.StartsWith(prefix, StringComparison.Ordinal));
    }
}
