using System.IO;

namespace SiNet.Domain.Files;

/// <summary>
/// Pure detector for the "extension conflict" rule enforced by the project-file write pipeline: a new
/// file may not be placed when a file with the <b>same base name</b> (case-insensitive, extension
/// removed) but a <b>different extension</b> already exists in the same folder. Mirrors the legacy
/// pre-upload guard in <c>ProjectFileNode.PlaceFileAsync</c> ("התנגשות סיומות") without any IO —
/// callers supply the candidate name and the names already present.
/// </summary>
public static class ProjectFileExtensionConflict
{
    /// <summary>
    /// Returns the first existing file name that shares <paramref name="candidateFileName"/>'s base
    /// name but carries a different extension, or <see langword="null"/> when there is no conflict.
    /// </summary>
    /// <param name="candidateFileName">The file name about to be placed.</param>
    /// <param name="existingFileNames">File names already present in the target folder.</param>
    public static string? FindConflict(string? candidateFileName, IEnumerable<string>? existingFileNames)
    {
        if (string.IsNullOrWhiteSpace(candidateFileName) || existingFileNames is null)
            return null;

        var candidateBase = Path.GetFileNameWithoutExtension(candidateFileName);
        var candidateExt = NormalizeExtension(candidateFileName);
        if (string.IsNullOrEmpty(candidateBase))
            return null;

        foreach (var existing in existingFileNames)
        {
            if (string.IsNullOrWhiteSpace(existing))
                continue;

            var existingBase = Path.GetFileNameWithoutExtension(existing);
            if (!string.Equals(existingBase, candidateBase, StringComparison.OrdinalIgnoreCase))
                continue;

            var existingExt = NormalizeExtension(existing);
            if (!string.Equals(existingExt, candidateExt, StringComparison.OrdinalIgnoreCase))
                return existing;
        }

        return null;
    }

    private static string NormalizeExtension(string fileName)
        => Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
}
