namespace MasterPlan.SyncEngine;

/// <summary>Result of preparing a chosen <c>.bak</c> for SQL <c>RESTORE</c> (DEV-020).</summary>
public sealed record MonthlyBackupStagingResult(
    string OriginalSourcePath,
    string ClientStagingFilePath,
    string ServerRestorePath,
    bool MovedIntoStaging);

/// <summary>
/// Moves (not copies) a monthly backup into the shared staging folder and maps
/// the client path to the SQL Server path. Prunes older <c>.bak</c> files.
/// </summary>
public static class MonthlyBackupStaging
{
    /// <summary>
    /// Ensures <paramref name="sourceBackupPath"/> is under the client staging folder
    /// (moving it when needed), prunes retention, and returns the server-side path
    /// for <c>RESTORE … FROM DISK</c>.
    /// </summary>
    public static MonthlyBackupStagingResult PrepareForSqlRestore(
        string sourceBackupPath,
        MonthlyBackupStagingOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBackupPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientStagingPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ServerStagingPath);

        if (options.MaxRetainedBackups < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxRetainedBackups,
                "MaxRetainedBackups must be at least 1.");
        }

        if (!File.Exists(sourceBackupPath))
        {
            throw new FileNotFoundException("קובץ הגיבוי לא נמצא.", sourceBackupPath);
        }

        var clientStagingRoot = NormalizeDirectory(options.ClientStagingPath);
        Directory.CreateDirectory(clientStagingRoot);

        var sourceFull = Path.GetFullPath(sourceBackupPath);
        var sourceDir = Path.GetDirectoryName(sourceFull)
            ?? throw new InvalidOperationException($"Cannot resolve directory for '{sourceFull}'.");

        var moved = false;
        string clientFilePath;

        if (PathsEqual(sourceDir, clientStagingRoot))
        {
            clientFilePath = sourceFull;
        }
        else
        {
            var fileName = Path.GetFileName(sourceFull);
            clientFilePath = Path.Combine(clientStagingRoot, fileName);

            if (File.Exists(clientFilePath) && !PathsEqual(clientFilePath, sourceFull))
            {
                // Keep the incoming file; avoid overwriting a different bak of the same name.
                var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var uniqueName =
                    $"{Path.GetFileNameWithoutExtension(fileName)}_{stamp}{Path.GetExtension(fileName)}";
                clientFilePath = Path.Combine(clientStagingRoot, uniqueName);
            }

            File.Move(sourceFull, clientFilePath, overwrite: false);
            moved = true;
        }

        PruneOlderBackups(clientStagingRoot, options.MaxRetainedBackups, keepFilePath: clientFilePath);

        var serverFilePath = ToServerRestorePath(clientFilePath, options);

        return new MonthlyBackupStagingResult(
            OriginalSourcePath: sourceFull,
            ClientStagingFilePath: clientFilePath,
            ServerRestorePath: serverFilePath,
            MovedIntoStaging: moved);
    }

    /// <summary>
    /// Keeps the newest <paramref name="maxRetained"/> <c>.bak</c> files (by write time),
    /// always retaining <paramref name="keepFilePath"/>. Deletes older files.
    /// </summary>
    public static IReadOnlyList<string> PruneOlderBackups(
        string clientStagingRoot,
        int maxRetained,
        string keepFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientStagingRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(keepFilePath);
        if (maxRetained < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetained));
        }

        var keepFull = Path.GetFullPath(keepFilePath);
        var ordered = Directory.EnumerateFiles(clientStagingRoot, "*.bak", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Always retain the bak we are about to restore; fill the rest with newest files.
        var retain = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { keepFull };
        foreach (var file in ordered)
        {
            if (retain.Count >= maxRetained)
            {
                break;
            }

            retain.Add(file.FullName);
        }

        var deleted = new List<string>();
        foreach (var file in ordered)
        {
            if (retain.Contains(file.FullName))
            {
                continue;
            }

            file.Delete();
            deleted.Add(file.FullName);
        }

        return deleted;
    }

    /// <summary>Maps a client staging file to the SQL Server path (same file name).</summary>
    public static string ToServerRestorePath(string clientStagingFilePath, MonthlyBackupStagingOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientStagingFilePath);
        ArgumentNullException.ThrowIfNull(options);

        return Path.Combine(
            NormalizeDirectory(options.ServerStagingPath),
            Path.GetFileName(clientStagingFilePath));
    }

    internal static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    internal static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
