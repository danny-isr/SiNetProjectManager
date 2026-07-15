using System.Diagnostics;

namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Default implementation of <see cref="IFileServerVersionArchiver"/>. Native port of the legacy
/// <c>FileServerVersionArchiver</c>. Archive layout: active file + companion JSON live in the slot
/// folder; previous versions move to a hidden <c>.versions</c> subfolder as <c>Name.vN.ext</c>.
/// A file without a companion JSON is treated as version 1.
/// </summary>
public sealed class FileServerVersionArchiver : IFileServerVersionArchiver
{
    private readonly IFileServerMetadataStore _metadataStore;

    public string VersionsFolderName => ".versions";

    public FileServerVersionArchiver(IFileServerMetadataStore metadataStore)
    {
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
    }

    public ArchiveResult? ArchiveIfExists(string activeFilePath)
    {
        if (string.IsNullOrWhiteSpace(activeFilePath))
            throw new ArgumentException("activeFilePath is required", nameof(activeFilePath));

        if (!File.Exists(activeFilePath))
            return null;

        var folder = Path.GetDirectoryName(activeFilePath)
            ?? throw new InvalidOperationException($"Cannot resolve folder for '{activeFilePath}'.");

        var versionsFolder = Path.Combine(folder, VersionsFolderName);
        EnsureHiddenFolder(versionsFolder);

        var existing = _metadataStore.TryRead(activeFilePath);
        var archivedVersionNumber = existing?.CurrentVersionNumber ?? 1;
        if (archivedVersionNumber < 1)
            archivedVersionNumber = 1;

        var archivedName = ProjectFileNameBuilder.BuildArchive(
            Path.GetFileName(activeFilePath),
            archivedVersionNumber);

        var archivedPath = Path.Combine(versionsFolder, archivedName);
        archivedPath = MakeUnique(archivedPath);

        File.Move(activeFilePath, archivedPath);

        var sourceJson = _metadataStore.GetMetadataPath(activeFilePath);
        if (File.Exists(sourceJson))
        {
            var targetJson = archivedPath + ".json";
            if (File.Exists(targetJson))
                File.Delete(targetJson);
            File.Move(sourceJson, targetJson);
        }

        return new ArchiveResult(
            ArchivedVersionNumber: archivedVersionNumber,
            ArchivedFilePath: archivedPath,
            NextActiveVersionNumber: archivedVersionNumber + 1);
    }

    private static void EnsureHiddenFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        try
        {
            var attrs = File.GetAttributes(folderPath);
            if ((attrs & FileAttributes.Hidden) == 0)
                File.SetAttributes(folderPath, attrs | FileAttributes.Hidden);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[FileServerVersionArchiver] Could not set Hidden attribute on '{folderPath}': {ex.Message}");
        }
    }

    private static string MakeUnique(string desiredPath)
    {
        if (!File.Exists(desiredPath))
            return desiredPath;

        var dir = Path.GetDirectoryName(desiredPath)!;
        var nameNoExt = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        return Path.Combine(dir, $"{nameNoExt}.{stamp}{ext}");
    }
}
