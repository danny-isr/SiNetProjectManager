using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Default implementation of <see cref="IFileServerMetadataStore"/>. Writes companion JSON as
/// <c>{activeFilePath}.json</c>. Failures are logged (Trace) but not thrown — metadata is
/// best-effort. Native port of the legacy <c>FileServerMetadataStore</c>.
/// </summary>
public sealed class FileServerMetadataStore : IFileServerMetadataStore
{
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string GetMetadataPath(string activeFilePath)
    {
        if (string.IsNullOrWhiteSpace(activeFilePath))
            throw new ArgumentException("activeFilePath is required", nameof(activeFilePath));
        return activeFilePath + ".json";
    }

    public FilePlacementMetadata? TryRead(string activeFilePath)
    {
        var jsonPath = GetMetadataPath(activeFilePath);
        if (!File.Exists(jsonPath))
            return null;

        try
        {
            var text = File.ReadAllText(jsonPath);
            return JsonSerializer.Deserialize<FilePlacementMetadata>(text);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[FileServerMetadataStore] Failed to read {jsonPath}: {ex.Message}");
            return null;
        }
    }

    public void Write(string activeFilePath, FilePlacementMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var jsonPath = GetMetadataPath(activeFilePath);
        try
        {
            if (File.Exists(jsonPath))
                File.Delete(jsonPath);

            var json = JsonSerializer.Serialize(metadata, s_writeOptions);
            File.WriteAllText(jsonPath, json);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[FileServerMetadataStore] Failed to write {jsonPath}: {ex.Message}");
        }
    }

    public void Delete(string activeFilePath)
    {
        var jsonPath = GetMetadataPath(activeFilePath);
        try
        {
            if (File.Exists(jsonPath))
                File.Delete(jsonPath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[FileServerMetadataStore] Failed to delete {jsonPath}: {ex.Message}");
        }
    }
}
