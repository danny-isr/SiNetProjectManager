namespace SiNet.Domain.Files;

/// <summary>
/// Parsed components of a project file name that follows the canonical pattern:
/// <c>(ProjectNumber)-ProjectType-Number-Alternative-Version-Name.ext</c>
/// <para>
/// The filename is the authoritative identity of a file across all storage destinations
/// (FileServer, ACC, GoogleDrive). If a filename does not match this pattern, it is considered
/// "unfiled" and belongs to no project file. Clean-layer port of the legacy
/// <c>SiNetSQL.FileIndex.ParsedFileName</c>.
/// </para>
/// </summary>
/// <param name="ProjectNumber">Owning project number parsed from the leading <c>(number)</c>.</param>
/// <param name="ProjectType">Project type / discipline id.</param>
/// <param name="Number">File number within the project/type.</param>
/// <param name="Alternative">Alternative (variant) label.</param>
/// <param name="Version">Version number.</param>
/// <param name="BaseName">Human-readable base name (may itself contain dashes).</param>
/// <param name="Extension">Normalized extension without the leading dot, lower-cased.</param>
public sealed record ParsedProjectFileName(
    int ProjectNumber,
    int ProjectType,
    int Number,
    string Alternative,
    int Version,
    string BaseName,
    string Extension);
