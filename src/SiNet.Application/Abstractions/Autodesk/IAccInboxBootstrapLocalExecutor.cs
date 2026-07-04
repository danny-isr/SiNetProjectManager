namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Host-specific local executor for ACC Inbox bootstrap when privileged ACC work is allowed to run
/// in-process. This remains a technical seam; callers should depend on
/// <see cref="IAccInboxBootstrapService"/>.
/// </summary>
public interface IAccInboxBootstrapLocalExecutor
{
    Task<AccInboxBootstrapResult> EnsureAsync(CancellationToken cancellationToken = default);
}
