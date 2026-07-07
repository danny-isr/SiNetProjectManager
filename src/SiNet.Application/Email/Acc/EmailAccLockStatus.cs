namespace SiNet.Application.Email.Acc;

/// <summary>
/// DB lease state for an inbox message upload operation.
/// </summary>
public sealed record EmailAccLockStatus(
    bool IsLocked,
    bool IsHeldByCurrentUser,
    string? ProcessingByLogin,
    DateTime? ProcessingStartedAtUtc,
    bool IsStaleLease);
