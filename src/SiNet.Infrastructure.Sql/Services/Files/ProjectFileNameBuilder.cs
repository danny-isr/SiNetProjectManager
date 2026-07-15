namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Builds filenames according to the project naming convention:
/// <c>(ProjectNumber)-ProjectType-FileNumber-Alternative-Version-Name.ext</c>.
/// Version is always 1 for new files — ACC handles its own versioning natively. Native port of the
/// legacy <c>SiNetSQL.Services.ProjectFileNameBuilder</c>.
/// </summary>
public static class ProjectFileNameBuilder
{
    public static string Build(
        int projectNumber,
        int projectType,
        int fileNumber,
        string alternative,
        string projectFileTitle,
        string originalFileName)
    {
        if (projectNumber <= 0 || fileNumber <= 0)
            return originalFileName;

        var name = !string.IsNullOrWhiteSpace(projectFileTitle)
            ? projectFileTitle.Trim()
            : Path.GetFileNameWithoutExtension(originalFileName);

        if (name.Length > 10)
            name = name.Substring(0, 10);

        var extension = Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();

        if (string.IsNullOrEmpty(alternative))
            alternative = "1";

        const int version = 1;

        return $"({projectNumber})-{projectType}-{fileNumber}-{alternative}-{version}-{name}.{extension}";
    }

    public static string BuildFolderName(
        int projectNumber,
        int projectType,
        int fileNumber,
        string alternative,
        string projectFileTitle,
        string originalFolderName)
    {
        if (projectNumber <= 0 || fileNumber <= 0)
            return originalFolderName;

        var name = !string.IsNullOrWhiteSpace(projectFileTitle)
            ? projectFileTitle.Trim()
            : originalFolderName;

        if (name.Length > 10)
            name = name.Substring(0, 10);

        if (string.IsNullOrEmpty(alternative))
            alternative = "1";

        const int version = 1;

        return $"({projectNumber})-{projectType}-{fileNumber}-{alternative}-{version}-{name}";
    }

    public static string BuildArchive(string activeFileName, int archivedVersionNumber)
    {
        if (string.IsNullOrWhiteSpace(activeFileName))
            throw new ArgumentException("activeFileName is required", nameof(activeFileName));
        if (archivedVersionNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(archivedVersionNumber), "Must be >= 1.");

        var nameNoExt = Path.GetFileNameWithoutExtension(activeFileName);
        var ext = Path.GetExtension(activeFileName);

        return string.IsNullOrEmpty(ext)
            ? $"{nameNoExt}.v{archivedVersionNumber}"
            : $"{nameNoExt}.v{archivedVersionNumber}{ext}";
    }
}
