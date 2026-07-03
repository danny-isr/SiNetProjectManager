namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>Resolved runtime mode for ACC privileged operations.</summary>
public enum AccServiceMode
{
    Local = 0,
    Remote = 1,
}

/// <summary>Health state of the remote AccService endpoint.</summary>
public enum AccServiceHealthState
{
    NotConfigured = 0,
    Online = 1,
    Offline = 2,
}

/// <summary>Health probe result for the remote AccService endpoint.</summary>
public sealed record AccServiceHealthResult(
    bool IsConfigured,
    AccServiceHealthState State,
    string? Endpoint,
    string? Detail);

/// <summary>Safe diagnostic metadata returned by the remote AccService `/diag` endpoint.</summary>
public sealed record AccServiceDiagnosticsResult(
    bool Reachable,
    string? WindowsUser,
    bool HasApiKey,
    string? KeySource,
    int KeyLength,
    string? KeyHashPrefix,
    bool AutodeskOk,
    string? AutodeskDetail,
    bool DbOk,
    string? DbDetail);

/// <summary>Safe local description of the configured AccService API key.</summary>
public sealed record AccServiceKeyInfo(
    bool HasApiKey,
    int KeyLength,
    string? KeyHashPrefix);
