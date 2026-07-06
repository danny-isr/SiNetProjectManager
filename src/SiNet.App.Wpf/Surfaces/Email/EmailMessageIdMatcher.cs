namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>Normalizes RFC message identifiers for correlation between SQL inbox rows and Gmail headers.</summary>
internal static class EmailMessageIdMatcher
{
    public static bool Matches(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) => value.Trim().Trim('<', '>');
}
