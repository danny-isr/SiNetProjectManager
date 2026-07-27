namespace SiNet.App.Composition;

/// <summary>
/// Explicit composition mode for the unified <c>AddSiNet</c> graph.
/// </summary>
public enum SiNetHostMode
{
    /// <summary>Clean new-system host (WPF harness / future standalone). No LegacyBridge.</summary>
    StandaloneNew = 0,

    /// <summary>V2 production host during Strangler. LegacyBridge opt-in allowed.</summary>
    V2Hybrid = 1,

    /// <summary>Headless service host (AccService / SyncEngine adapters).</summary>
    Service = 2,
}
