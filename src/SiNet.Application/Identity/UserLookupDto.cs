namespace SiNet.Application.Identity;

/// <summary>Lightweight active-user row for lookup dropdowns (no EF types).</summary>
public sealed record UserLookupDto(int UserId, string DisplayName, bool IsActive);
