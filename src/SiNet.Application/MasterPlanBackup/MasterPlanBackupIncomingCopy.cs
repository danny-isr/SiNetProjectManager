using System.Security.Cryptography;

namespace SiNet.Application.MasterPlanBackup;

/// <summary>
/// Desktop-safe copy into Incoming via <c>*.partial</c> then atomic rename.
/// Never moves or deletes the user's source file. Never runs SQL RESTORE.
/// </summary>
public static class MasterPlanBackupIncomingCopy
{
    public const string IncomingFolderName = "Incoming";
    public const string ProcessingFolderName = "Processing";
    public const string ProcessedFolderName = "Processed";
    public const string RejectedFolderName = "Rejected";
    public const string PartialExtension = ".partial";

    /// <summary>Shared staging root (spelling Bakup is production). Never N:\.</summary>
    public const string DefaultInboxRoot = @"D:\SharedFolder\ProjectsData\MasterPlanBakup";

    public static void EnsureLayout(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Directory.CreateDirectory(Path.Combine(root, IncomingFolderName));
        Directory.CreateDirectory(Path.Combine(root, ProcessingFolderName));
        Directory.CreateDirectory(Path.Combine(root, ProcessedFolderName));
        Directory.CreateDirectory(Path.Combine(root, RejectedFolderName));
    }

    public static string IncomingPath(string root, string fileName) =>
        Path.Combine(root, IncomingFolderName, fileName);

    public static bool IsPartialFile(string path) =>
        Path.GetExtension(path).Equals(PartialExtension, StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".bak" + PartialExtension, StringComparison.OrdinalIgnoreCase);

    public static bool IsCompletedBak(string path) =>
        Path.GetExtension(path).Equals(".bak", StringComparison.OrdinalIgnoreCase)
        && !IsPartialFile(path);

    public static async Task<(string DestinationBak, string Sha256)> CopyAtomicallyAsync(
        string sourcePath,
        string incomingRoot,
        string destinationFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(incomingRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFileName);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("קובץ הגיבוי לא נמצא.", sourcePath);

        EnsureLayout(incomingRoot);
        var incomingDir = Path.Combine(incomingRoot, IncomingFolderName);
        var bakName = destinationFileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
            ? destinationFileName
            : destinationFileName + ".bak";
        var destBak = Path.Combine(incomingDir, bakName);
        var destPartial = destBak + PartialExtension;

        if (File.Exists(destPartial))
            File.Delete(destPartial);

        await using (var source = File.OpenRead(sourcePath))
        await using (var dest = new FileStream(destPartial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
            await dest.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(destBak))
            File.Delete(destBak);
        File.Move(destPartial, destBak);

        var sha = await ComputeSha256Async(destBak, cancellationToken).ConfigureAwait(false);
        return (destBak, sha);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
