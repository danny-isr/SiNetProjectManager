using System.Text;

namespace SiNet.Application.Projects;

/// <summary>
/// Shared folder-name formulas for FileServer / ACC / Drive project roots
/// (legacy <c>FixDirectoryName</c> + <c>(Number)Title</c>).
/// </summary>
public static class ProjectFolderNameHelper
{
    public static string BuildNameAndNumber(int projectNumber, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return $"({projectNumber}){title.Trim()}";
    }

    public static string? FixDirectoryName(string? nameVal)
    {
        if (string.IsNullOrEmpty(nameVal))
            return null;

        return RemoveInvalidDirectoryChars(
                nameVal.Trim()
                    .Replace("    ", " ", StringComparison.Ordinal)
                    .Replace("   ", " ", StringComparison.Ordinal)
                    .Replace("  ", " ", StringComparison.Ordinal))
            ?.Replace(" ", "_", StringComparison.Ordinal);
    }

    private static string? RemoveInvalidDirectoryChars(string? nameVal)
    {
        if (string.IsNullOrEmpty(nameVal))
            return null;

        var sb = new StringBuilder(nameVal.Length);
        foreach (var ch in nameVal.Trim())
        {
            if (ch is '\\' or '/' or '"' or ':' or '*' or '?' or '<' or '>' or '|')
                continue;
            sb.Append(ch);
        }

        return sb.ToString();
    }
}
