using System.Text.RegularExpressions;

namespace SiNet.Domain.Files;

/// <summary>
/// AutoCAD recover naming as observed in the office (DEV-003): 
/// <c>{Primary}_recover.dwg</c>, <c>{Primary}_recover000.dwg</c>, …
/// Pure string logic — no IO.
/// </summary>
public static partial class RecoverFileNaming
{
    /// <summary>
    /// Matches <c>_recover</c> optionally followed by up to 3 digits, immediately before the extension.
    /// </summary>
    [GeneratedRegex(@"_recover(\d{0,3})(?=\.[^.]+$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RecoverSuffixRegex { get; }

    public static bool IsRecoverFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && RecoverSuffixRegex.IsMatch(fileName);

    /// <summary>
    /// Strips the <c>_recover</c> / <c>_recoverNNN</c> suffix before the extension.
    /// Returns <see langword="false"/> when the name is not a recover file.
    /// </summary>
    public static bool TryGetPrimaryFileName(string? recoverFileName, out string primaryFileName)
    {
        primaryFileName = string.Empty;
        if (string.IsNullOrWhiteSpace(recoverFileName) || !IsRecoverFileName(recoverFileName))
        {
            return false;
        }

        primaryFileName = RecoverSuffixRegex.Replace(recoverFileName, string.Empty);
        return !string.IsNullOrWhiteSpace(primaryFileName);
    }
}
