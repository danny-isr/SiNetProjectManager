namespace SiNet.Infrastructure.Autodesk;

/// <summary>Host-tunable runtime settings for the ACC control-plane HTTP clients.</summary>
public sealed class AccServiceControlPlaneOptions
{
    public TimeSpan HealthTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan DiagnosticsTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// HttpClient timeout for ACC file upload/download via SiOffice.AccService.
    /// Large attachments (e.g. DWF) can exceed the .NET default of 100 seconds.
    /// </summary>
    public TimeSpan FileTransferTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    public IReadOnlyList<string> ApprovedSelfSignedHosts { get; set; } =
    [
        "SI-WIN-2K19",
        "localhost",
        "127.0.0.1",
    ];

    public IReadOnlyList<string> ApprovedSelfSignedHostSuffixes { get; set; } =
    [
        ".si-eng.local",
    ];

    public IReadOnlyList<string> ApprovedSelfSignedIpPrefixes { get; set; } =
    [
        "192.168.",
    ];
}
