using System.IO;

namespace SiNet.Domain.Files;

/// <summary>
/// Pure builder for canonical project file names, the inverse of <see cref="ProjectFileNameParser"/>:
/// <c>(ProjectNumber)-ProjectType-Number-Alternative-Version-Name.ext</c>.
/// <para>
/// The filename is the authoritative identity of a file across every storage destination, so the
/// write pipeline (add alternative / add version / replace) uses this builder to produce the name a
/// staged file must carry before it is uploaded. Mirrors the naming rules of the legacy
/// <c>SiNetSQL.Services.ProjectFileNameBuilder</c> / <c>BaseFileVersion</c>, except the base-name
/// cap is <see cref="MaxBaseNameLength"/> (derived from live <c>ProjectFile.Title</c> lengths,
/// not the legacy hard-coded 10).
/// </para>
/// </summary>
public static class ProjectFileNameBuilder
{
    /// <summary>
    /// Maximum length of the human-readable base-name segment.
    /// Set to (max <c>LEN(ProjectFile.Title)</c> in SIData) + 2 — measured 2026-07-31 as 33 → 35.
    /// Legacy SiNetSQL still truncates at 10.
    /// </summary>
    public const int MaxBaseNameLength = 35;

    /// <summary>
    /// Builds a canonical file name. When <paramref name="projectNumber"/> or
    /// <paramref name="fileNumber"/> is non-positive the convention cannot be formed and the original
    /// file name is returned unchanged (an "unfiled" placement).
    /// </summary>
    /// <param name="projectNumber">Owning project number.</param>
    /// <param name="projectType">Project type / discipline id.</param>
    /// <param name="fileNumber">File number within the project/type.</param>
    /// <param name="alternative">Alternative label (defaults to <c>"1"</c> when empty).</param>
    /// <param name="version">Version number (defaults to 1 when non-positive).</param>
    /// <param name="projectFileTitle">Preferred base name; falls back to the source file's stem.</param>
    /// <param name="originalFileName">The source file name, used for the extension and title fallback.</param>
    public static string Build(
        int projectNumber,
        int projectType,
        int fileNumber,
        string? alternative,
        int version,
        string? projectFileTitle,
        string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new ArgumentException("originalFileName is required.", nameof(originalFileName));

        if (projectNumber <= 0 || fileNumber <= 0)
            return originalFileName;

        var name = !string.IsNullOrWhiteSpace(projectFileTitle)
            ? projectFileTitle!.Trim()
            : Path.GetFileNameWithoutExtension(originalFileName);

        if (name.Length > MaxBaseNameLength)
            name = name[..MaxBaseNameLength];

        var extension = Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();

        var alt = string.IsNullOrWhiteSpace(alternative) ? "1" : alternative!.Trim();
        var ver = version <= 0 ? 1 : version;

        return $"({projectNumber})-{projectType}-{fileNumber}-{alt}-{ver}-{name}.{extension}";
    }

    /// <summary>
    /// Builds the name of the next version for an existing parsed file, keeping every identity
    /// component and only advancing the version number.
    /// </summary>
    public static string BuildNextVersion(ParsedProjectFileName existing, int nextVersion)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var ver = nextVersion <= 0 ? existing.Version + 1 : nextVersion;
        return $"({existing.ProjectNumber})-{existing.ProjectType}-{existing.Number}-{existing.Alternative}-{ver}-{existing.BaseName}.{existing.Extension}";
    }
}
