namespace SiNet.Domain.Files;

/// <summary>How a recover file should appear in the ProjectWork tree (DEV-003 §4.2).</summary>
public enum RecoverTreeRole
{
    /// <summary>Not a recover file — normal visibility.</summary>
    NotRecover,

    /// <summary>Stale / 0-byte / non-best variant — do not show.</summary>
    Hidden,

    /// <summary>Paired and newer than primary — show green.</summary>
    ActionableNewer,

    /// <summary>No primary in the same folder — show orange; never bulk-delete.</summary>
    Orphan,
}

/// <summary>
/// Classifies recover files in one folder's scan results. Pairing is same-directory by stripped name.
/// </summary>
public static class RecoverScanClassifier
{
    public readonly record struct FileStamp(string FileName, long SizeBytes, DateTime? LastModified);

    public static IReadOnlyDictionary<string, RecoverTreeRole> Classify(IEnumerable<FileStamp> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var list = files
            .Where(f => !string.IsNullOrWhiteSpace(f.FileName))
            .GroupBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var byName = list.ToDictionary(f => f.FileName, StringComparer.OrdinalIgnoreCase);
        var roles = new Dictionary<string, RecoverTreeRole>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in list)
        {
            if (!RecoverFileNaming.IsRecoverFileName(file.FileName))
            {
                roles[file.FileName] = RecoverTreeRole.NotRecover;
            }
        }

        var families = new Dictionary<string, List<FileStamp>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in list)
        {
            if (!RecoverFileNaming.TryGetPrimaryFileName(file.FileName, out var primaryName))
            {
                continue;
            }

            if (!families.TryGetValue(primaryName, out var members))
            {
                members = [];
                families[primaryName] = members;
            }

            members.Add(file);
        }

        foreach (var (primaryName, members) in families)
        {
            var hasPrimary = byName.TryGetValue(primaryName, out var primary);
            if (!hasPrimary)
            {
                foreach (var member in members)
                {
                    roles[member.FileName] = RecoverFileRelevance.IsZeroByte(member.SizeBytes)
                        ? RecoverTreeRole.Hidden
                        : RecoverTreeRole.Orphan;
                }

                continue;
            }

            var nonEmpty = members.Where(m => !RecoverFileRelevance.IsZeroByte(m.SizeBytes)).ToList();
            foreach (var member in members.Where(m => RecoverFileRelevance.IsZeroByte(m.SizeBytes)))
            {
                roles[member.FileName] = RecoverTreeRole.Hidden;
            }

            if (nonEmpty.Count == 0)
            {
                continue;
            }

            var primaryTime = primary.LastModified ?? DateTime.MinValue;
            var best = nonEmpty
                .OrderByDescending(m => m.LastModified ?? DateTime.MinValue)
                .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
                .First();

            var bestTime = best.LastModified ?? DateTime.MinValue;
            var bestIsActionable = RecoverFileRelevance.IsActionableNewerThanPrimary(
                best.SizeBytes,
                bestTime,
                primaryTime);

            foreach (var member in nonEmpty)
            {
                if (bestIsActionable
                    && string.Equals(member.FileName, best.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    roles[member.FileName] = RecoverTreeRole.ActionableNewer;
                }
                else
                {
                    roles[member.FileName] = RecoverTreeRole.Hidden;
                }
            }
        }

        return roles;
    }
}
