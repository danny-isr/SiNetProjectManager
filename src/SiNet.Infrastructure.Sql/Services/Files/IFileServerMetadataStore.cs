namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Reads and writes the companion JSON metadata file for a placed FileServer file. The JSON file
/// lives next to the active file and is named <c>{activeFilePath}.json</c>.
/// </summary>
public interface IFileServerMetadataStore
{
    string GetMetadataPath(string activeFilePath);
    FilePlacementMetadata? TryRead(string activeFilePath);
    void Write(string activeFilePath, FilePlacementMetadata metadata);
    void Delete(string activeFilePath);
}
