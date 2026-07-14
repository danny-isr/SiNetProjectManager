namespace SiNet.Application.Runtime;

/// <summary>Unified runtime state for a subsystem shown in New System «מצב מערכת».</summary>
public enum SubsystemRuntimeState
{
    Running = 0,
    Idle = 1,
    Degraded = 2,
    Stopped = 3,
    NotConfigured = 4,
}
