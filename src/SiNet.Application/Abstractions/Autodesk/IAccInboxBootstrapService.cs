namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>
/// Privileged ACC write operation that ensures the Office Inbox ACC project, root folder, inbox
/// folder, and required access exist.
/// </summary>
public interface IAccInboxBootstrapService
{
    Task<AccInboxBootstrapResult> EnsureAsync(CancellationToken cancellationToken = default);
}
