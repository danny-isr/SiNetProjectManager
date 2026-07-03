namespace SiNet.Infrastructure.Autodesk;

/// <summary>Host-tunable runtime settings for the ACC control-plane HTTP clients.</summary>
public sealed class AccServiceControlPlaneOptions
{
    public TimeSpan HealthTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan DiagnosticsTimeout { get; set; } = TimeSpan.FromSeconds(10);

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
