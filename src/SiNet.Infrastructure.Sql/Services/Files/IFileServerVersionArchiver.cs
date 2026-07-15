namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Archives a previous active FileServer file into the centralized <c>.versions</c> subfolder
/// (hidden directory) before a new active file replaces it.
/// </summary>
public interface IFileServerVersionArchiver
{
    string VersionsFolderName { get; }
    ArchiveResult? ArchiveIfExists(string activeFilePath);
}

/// <summary>Outcome of an archive operation.</summary>
public readonly record struct ArchiveResult(
    int ArchivedVersionNumber,
    string ArchivedFilePath,
    int NextActiveVersionNumber);
