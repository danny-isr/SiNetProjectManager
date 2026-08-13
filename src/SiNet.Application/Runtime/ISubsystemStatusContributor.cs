namespace SiNet.Application.Runtime;

/// <summary>
/// Contributes one «מצב מערכת» row. Implemented in the infrastructure project that already owns the
/// dependency being probed, so the WPF layer never takes a direct SQL/Google/Autodesk reference
/// (see <c>docs/SYSTEM_HEALTH.md</c> §2).
/// <para>
/// <see cref="Key"/> and <see cref="DisplayNameHe"/> are declared up front so the aggregator can
/// render a failure row for a contributor that throws or times out.
/// </para>
/// </summary>
public interface ISubsystemStatusContributor
{
    /// <summary>Row identity. Must match the legacy health-check key when one exists, so the two sources collapse to a single row.</summary>
    string Key { get; }

    string DisplayNameHe { get; }

    /// <summary>Fast (default) runs every 5-minute cycle. Deep runs on startup, every 30 minutes, and on Refresh.</summary>
    SubsystemProbeTier Tier => SubsystemProbeTier.Fast;

    Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Default forwards to <see cref="ContributeAsync(CancellationToken)"/>. Contributors that do extra
    /// Deep work (AccService diag) override this.
    /// </summary>
    Task<SubsystemRuntimeStatus> ContributeAsync(
        SubsystemProbeContext context,
        CancellationToken cancellationToken = default)
        => ContributeAsync(cancellationToken);
}
