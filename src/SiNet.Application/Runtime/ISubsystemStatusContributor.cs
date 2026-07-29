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

    Task<SubsystemRuntimeStatus> ContributeAsync(CancellationToken cancellationToken = default);
}
