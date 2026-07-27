using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SiNet.Infrastructure.Autodesk;

public static class AccServiceHttpClientConfigurator
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
            ServerCertificateCustomValidationCallback = (message, certificate, _, errors) =>
                ValidateServerCertificate(message, certificate, errors, options),
        };
    }

    public static bool ValidateServerCertificate(
        HttpRequestMessage? message,
        X509Certificate2? certificate,
        SslPolicyErrors errors,
        AccServiceControlPlaneOptions options)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (errors == SslPolicyErrors.RemoteCertificateChainErrors)
        {
            if (message?.RequestUri?.IsLoopback == true)
            {
                return true;
            }

            return IsPinnedThumbprint(certificate, options.PinnedCertificateThumbprints);
        }

        return false;
    }

    private static bool IsPinnedThumbprint(
        X509Certificate2? certificate,
        IReadOnlyList<string> pinnedThumbprints)
    {
        if (certificate is null || pinnedThumbprints.Count == 0)
        {
            return false;
        }

        var serverThumbprint = NormalizeThumbprint(certificate.Thumbprint);
        return pinnedThumbprints.Any(pin =>
            string.Equals(NormalizeThumbprint(pin), serverThumbprint, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeThumbprint(string? thumbprint) =>
        thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal) ?? string.Empty;
}
