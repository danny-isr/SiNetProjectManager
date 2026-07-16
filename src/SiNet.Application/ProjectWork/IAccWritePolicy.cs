namespace SiNet.Application.ProjectWork;

/// <summary>
/// The ACC-write gate for the ProjectWork surface. Every ACC write path (upload / add-version /
/// replace / hide-delete / manual-upload) must consult this policy and fail-fast when writes are not
/// enabled, so the gated write phase can be shipped dark and flipped on only after the ACC-Write-Policy
/// is explicitly approved (see <c>docs/PHASE_E_GATED.md</c> and <c>docs/ACC_BOUNDARY.md</c>).
/// <para>
/// FileServer writes are <b>not</b> gated by this policy — they were always allowed. Only ACC-destined
/// writes are subject to the gate.
/// </para>
/// </summary>
public interface IAccWritePolicy
{
    /// <summary>Whether ACC write operations are currently permitted.</summary>
    bool IsWriteEnabled { get; }

    /// <summary>
    /// Throws <see cref="AccWriteGatedException"/> when ACC writes are disabled. Call at the entry of
    /// every ACC write operation.
    /// </summary>
    void EnsureWriteAllowed(string operation);
}

/// <summary>
/// Raised when an ACC write is attempted while the ACC-write gate is closed. Distinct from
/// <see cref="NotSupportedException"/> so the UI can surface a clear "writes are disabled" message
/// rather than a generic failure.
/// </summary>
public sealed class AccWriteGatedException : InvalidOperationException
{
    public AccWriteGatedException(string operation)
        : base($"ACC write operation '{operation}' is blocked: the ACC-write gate is closed. " +
               "Enable the approved ACC-Write-Policy before performing ACC writes from ProjectWork.")
        => Operation = operation;

    /// <summary>The operation name that was blocked.</summary>
    public string Operation { get; }
}

/// <summary>
/// Fixed <see cref="IAccWritePolicy"/> whose enabled state is decided once at construction. The default
/// composition registers this closed (writes disabled); a host may register an alternative
/// configuration-driven policy to open the gate after approval.
/// </summary>
public sealed class StaticAccWritePolicy : IAccWritePolicy
{
    public StaticAccWritePolicy(bool isWriteEnabled) => IsWriteEnabled = isWriteEnabled;

    /// <inheritdoc />
    public bool IsWriteEnabled { get; }

    /// <inheritdoc />
    public void EnsureWriteAllowed(string operation)
    {
        if (!IsWriteEnabled)
            throw new AccWriteGatedException(operation);
    }
}
