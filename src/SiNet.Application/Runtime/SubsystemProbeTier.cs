namespace SiNet.Application.Runtime;

/// <summary>
/// How often a «מצב מערכת» contributor should run. Fast stays on the 5-minute loop; Deep runs on
/// the first probe, every 30 minutes, and whenever the user clicks Refresh.
/// </summary>
public enum SubsystemProbeTier
{
    Fast = 0,
    Deep = 1,
}

/// <summary>Per-cycle flags passed to contributors that opt into extra Deep work.</summary>
public sealed record SubsystemProbeContext(bool IncludeDeep);
