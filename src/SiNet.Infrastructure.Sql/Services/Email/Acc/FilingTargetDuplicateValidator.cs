namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>
/// Shared duplicate-target validation for batch filing flows. Native port of the legacy
/// <c>SiNetSQL.Services.Files.FilingTargetDuplicateValidator</c>.
/// <para>
/// Rule: within the same batch, no two source items may target the same
/// (<c>ProjectFileId</c>, <c>ProjectAlternativeId</c>) pair. Same <c>ProjectFileId</c>
/// with a different alternative is allowed.
/// </para>
/// </summary>
public static class FilingTargetDuplicateValidator
{
    /// <summary>
    /// Target identity used for duplicate detection. A <c>null</c> alternative is treated
    /// as the project default and is distinct from any explicit alternative id.
    /// </summary>
    public readonly record struct TargetKey(int ProjectFileId, int? ProjectAlternativeId);

    /// <summary>One conflicting group: a target key and the source labels that all map to it.</summary>
    public sealed record DuplicateGroup(TargetKey Key, IReadOnlyList<string> SourceLabels);

    /// <summary>
    /// Returns one entry per duplicated target. Inputs whose <see cref="TargetKey.ProjectFileId"/>
    /// is zero or negative are ignored (treated as "not yet tagged").
    /// </summary>
    public static IReadOnlyList<DuplicateGroup> FindDuplicates(
        IEnumerable<(TargetKey Target, string SourceLabel)> items)
    {
        return items
            .Where(i => i.Target.ProjectFileId > 0)
            .GroupBy(i => i.Target)
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup(g.Key, g.Select(x => x.SourceLabel).ToList()))
            .ToList();
    }

    /// <summary>Standard user-facing error message (Hebrew) describing the conflict.</summary>
    public const string UserMessageHebrew =
        "לא ניתן להעביר שני קבצים באותה פעולה לאותו קובץ יעד ולאותה אלטרנטיבה. " +
        "בחר אלטרנטיבה אחרת או יעד אחר לאחד הקבצים.";

    /// <summary>Builds a human-readable details string listing the conflicting groups.</summary>
    public static string FormatDetails(IReadOnlyList<DuplicateGroup> groups)
    {
        return string.Join("; ", groups.Select(g =>
            $"ProjectFileId={g.Key.ProjectFileId}, AltId=" +
            $"{(g.Key.ProjectAlternativeId.HasValue ? g.Key.ProjectAlternativeId.Value.ToString() : "default")}" +
            $" -> [{string.Join(", ", g.SourceLabels)}]"));
    }
}
