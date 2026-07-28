namespace SiOffice.AccService;

/// <summary>
/// Holds the thumbprint of the certificate Kestrel is presenting. Safe to expose in logs and /diag
/// — it is a trust pin, not a secret.
/// </summary>
internal static class AccServiceRuntimeTlsState
{
    public static string? CertificateThumbprint { get; set; }
}
