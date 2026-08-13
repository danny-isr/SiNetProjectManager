using System.Globalization;
using System.Text;

namespace SiNet.Application.Email;

/// <summary>Small Hebrew-aware edit-distance helper for optional place-folder notes (DEV-026).</summary>
internal static class HebrewLabelSimilarity
{
    public static bool IsClose(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0 || string.Equals(a, b, StringComparison.Ordinal))
        {
            return false;
        }

        var distance = Levenshtein(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return distance == 1 || (maxLen >= 5 && distance == 2);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var formD = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch is '\'' or '"' or '׳' or '״' or '’' or '“' or '”')
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }

                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }
}
