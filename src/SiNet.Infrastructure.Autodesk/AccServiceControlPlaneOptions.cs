namespace SiNet.Infrastructure.Autodesk;

/// <summary>Host-tunable runtime settings for the ACC control-plane HTTP clients.</summary>
public sealed class AccServiceControlPlaneOptions
{
    public TimeSpan HealthTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan DiagnosticsTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// HttpClient timeout for AccService control-plane calls (browse, bootstrap, folder path, etc.).
    /// Keeps email selection / status sync from hanging when AccService is unreachable.
    /// Does <b>not</b> apply to file upload/download — see <see cref="FileTransferTimeout"/>.
    /// </summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// HttpClient timeout for ACC file upload/download via SiOffice.AccService.
    /// Large attachments (e.g. DWF) can exceed the .NET default of 100 seconds.
    /// </summary>
    public TimeSpan FileTransferTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Optional TLS thumbprint pins for SiOffice.AccService HTTPS endpoints.
    /// When non-empty, chain errors are accepted only when the server certificate
    /// thumbprint matches one of these values. When empty, only valid CA chains
    /// or loopback hosts are accepted on chain errors.
    /// </summary>
    public IReadOnlyList<string> PinnedCertificateThumbprints { get; set; } = [];
}
