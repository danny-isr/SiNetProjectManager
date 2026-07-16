using System.IO;

namespace SiNet.Domain.Files;

/// <summary>
/// Pure parser for project file names following the canonical pattern:
/// <c>(ProjectNumber)-ProjectType-Number-Alternative-Version-Name.ext</c>.
/// <para>
/// Returns <see langword="null"/> when the filename does not match the pattern; callers should treat
/// those files as "unfiled" (not belonging to the project). Clean-layer port of the legacy
/// <c>SiNetSQL.FileIndex.ProjectFileNameParser</c> — identical parsing rules, no external dependencies.
/// </para>
/// </summary>
public static class ProjectFileNameParser
{
    /// <summary>
    /// Attempts to parse a filename into its <see cref="ParsedProjectFileName"/> components.
    /// Returns <see langword="null"/> if the name does not match the project file pattern.
    /// </summary>
    /// <param name="fileName">File name with extension (a directory component is stripped if present).</param>
    public static ParsedProjectFileName? TryParse(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var name = Path.GetFileName(fileName);
        var extension = Path.GetExtension(name);
        if (string.IsNullOrEmpty(extension))
            return null;

        var stem = Path.GetFileNameWithoutExtension(name);

        // Must start with "(number)".
        if (stem.Length < 3 || stem[0] != '(')
            return null;

        var closeParen = stem.IndexOf(')');
        if (closeParen <= 1)
            return null;

        var projNumStr = stem.Substring(1, closeParen - 1);
        if (!int.TryParse(projNumStr, out var projectNumber))
            return null;

        // Remainder after ")" — expect "-type-number-alt-version-baseName".
        var rest = stem[(closeParen + 1)..];
        if (!rest.StartsWith('-'))
            return null;
        rest = rest[1..];

        // Split the first four segments; the base name may contain further dashes.
        var parts = rest.Split('-', 5);
        if (parts.Length < 5)
            return null;

        if (!int.TryParse(parts[0], out var projectType))
            return null;
        if (!int.TryParse(parts[1], out var number))
            return null;

        var alternative = parts[2];
        if (string.IsNullOrEmpty(alternative))
            return null;

        if (!int.TryParse(parts[3], out var version))
            return null;

        var baseName = parts[4];
        if (string.IsNullOrEmpty(baseName))
            return null;

        var ext = extension.TrimStart('.').ToLowerInvariant();

        return new ParsedProjectFileName(projectNumber, projectType, number, alternative, version, baseName, ext);
    }
}
