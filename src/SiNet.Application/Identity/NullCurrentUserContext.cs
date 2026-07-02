namespace SiNet.Application.Identity;

/// <summary>
/// Default no-op current user context for hosts/tests without authenticated user binding.
/// </summary>
public sealed class NullCurrentUserContext : ICurrentUserContext
{
    public static NullCurrentUserContext Instance { get; } = new();

    public int? UserId => null;
}
