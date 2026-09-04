namespace SiNet.Application.Identity;

/// <summary>Trim + ordinal-ignore-case equality for SIUser.Email vs external account emails.</summary>
public static class IdentityEmailComparer
{
    public static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return email.Trim();
    }

    public static bool EqualsNormalized(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a is null || b is null)
        {
            return false;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
