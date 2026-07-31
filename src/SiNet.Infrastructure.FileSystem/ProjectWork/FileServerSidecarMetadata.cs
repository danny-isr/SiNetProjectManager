using System.IO;
using System.Text.Json;

namespace SiNet.Infrastructure.FileSystem.ProjectWork;

/// <summary>
/// Minimal reader for the hidden <c>{fileName}.si.json</c> sidecar companion used on the file server.
/// Clean-layer port of the read-side of the legacy <c>SiNetSQL.FileIndex.SidecarMetadata</c>. Only the
/// fields needed by read-only scanning are surfaced here (source file name); write-side concerns are
/// deferred to the gated write phase.
/// </summary>
public static class FileServerSidecarMetadata
{
    /// <summary>The suffix that identifies a SiNet sidecar companion file.</summary>
    public const string SidecarSuffix = ".si.json";

    /// <summary>
    /// Returns <see langword="true"/> when the path/name must not appear in a ProjectWork file scan
    /// (sidecar companions + ephemeral Office owner/lock files).
    /// </summary>
    public static bool ShouldSkipFromScan(string fullPathOrName) =>
        IsMetadataCompanion(fullPathOrName) || IsOfficeOwnerLockFile(fullPathOrName);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="fullPathOrName"/> is a metadata companion that
    /// should be skipped during scanning: either a SiNet sidecar (<c>*.si.json</c>) or a
    /// <c>{data}.json</c> companion sitting next to its data sibling.
    /// </summary>
    public static bool IsMetadataCompanion(string fullPathOrName)
    {
        if (string.IsNullOrWhiteSpace(fullPathOrName))
            return false;

        var name = Path.GetFileName(fullPathOrName);
        if (name.EndsWith(SidecarSuffix, StringComparison.OrdinalIgnoreCase))
            return true;

        // A "{data}.{ext}.json" companion that sits directly beside its "{data}.{ext}" sibling.
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && Path.IsPathRooted(fullPathOrName))
        {
            var sibling = fullPathOrName[..^".json".Length];
            if (File.Exists(sibling))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Word / Excel / PowerPoint create a short-lived owner file <c>~$Document.docx</c> next to the
    /// real document while it is open. It is not a project deliverable and must not appear in the tree
    /// (looks like a duplicate of the open file).
    /// </summary>
    public static bool IsOfficeOwnerLockFile(string fullPathOrName)
    {
        if (string.IsNullOrWhiteSpace(fullPathOrName))
            return false;

        var name = Path.GetFileName(fullPathOrName);
        return name.StartsWith("~$", StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes (or overwrites) the <c>{fullPath}.si.json</c> sidecar recording the original source file
    /// name for the placed data file. Best-effort: a failure to write the sidecar must not fail the
    /// placement, so exceptions are swallowed.
    /// </summary>
    public static void WriteSourceFileName(string fullPath, string? sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(sourceFileName))
            return;

        try
        {
            var payload = new Dictionary<string, string?>
            {
                ["SourceFileName"] = sourceFileName,
                ["PlacedAtUtc"] = DateTime.UtcNow.ToString("o"),
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fullPath + SidecarSuffix, json);
        }
        catch
        {
            // Sidecar is provenance metadata only; never fail a placement because of it.
        }
    }

    /// <summary>
    /// Reads the original source file name recorded in <c>{fullPath}.si.json</c>, or
    /// <see langword="null"/> when the sidecar is missing or unreadable.
    /// </summary>
    public static string? TryReadSourceFileName(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        var sidecarPath = fullPath + SidecarSuffix;
        if (!File.Exists(sidecarPath))
            return null;

        try
        {
            using var stream = File.OpenRead(sidecarPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            foreach (var propertyName in new[] { "SourceFileName", "sourceFileName" })
            {
                if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
        }
        catch
        {
            // Corrupt / partially-written sidecar — treat as absent.
        }

        return null;
    }
}
