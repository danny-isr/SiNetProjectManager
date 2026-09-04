namespace SiNet.Application.Identity;

/// <summary>
/// Fail-closed gate before connector / business writes.
/// Pending, incomplete, mismatched, or blocked identities never reach external side effects.
/// </summary>
public interface IIdentityOperationGuard
{
    /// <summary>
    /// Ensures the current identity may perform <paramref name="kind"/>.
    /// Throws <see cref="IdentityOperationDeniedException"/> when denied.
    /// </summary>
    Task EnsureAllowedAsync(IdentityOperationKind kind, CancellationToken cancellationToken = default);

    /// <summary>Non-throwing check; never performs external writes.</summary>
    Task<IdentityGuardDecision> EvaluateAsync(
        IdentityOperationKind kind,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of an identity operation guard check.</summary>
public sealed record IdentityGuardDecision(bool Allowed, string? Reason, IdentityCoherenceSnapshot Snapshot);

/// <summary>Thrown when a connector/business write is blocked by identity coherence.</summary>
public sealed class IdentityOperationDeniedException : InvalidOperationException
{
    public IdentityOperationDeniedException(string message, IdentityCoherenceSnapshot snapshot)
        : base(message)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public IdentityCoherenceSnapshot Snapshot { get; }
}
