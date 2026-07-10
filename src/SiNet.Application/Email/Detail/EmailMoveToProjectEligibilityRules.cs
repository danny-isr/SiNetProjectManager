namespace SiNet.Application.Email.Detail;

/// <summary>
/// Shared eligibility messages and duplicate-target detection for MoveToProject.
/// Mirrors legacy <c>FilingTargetDuplicateValidator</c> wording and key rules.
/// </summary>
public static class EmailMoveToProjectEligibilityRules
{
    public const string DuplicateTargetMessage =
        "לא ניתן להעביר שני קבצים באותה פעולה לאותו קובץ יעד ולאותה אלטרנטיבה. " +
        "בחר אלטרנטיבה אחרת או יעד אחר לאחד הקבצים.";

    public static string UntaggedAttachmentsMessage(int count) =>
        $"נותרו {count} צרופות לא מתויגות. בחר קובץ פרויקט (חומר חיצוני) לכל צרופה.";

    /// <summary>
    /// True when two or more tagged items share the same
    /// (<paramref name="targets"/> ProjectFileId, ProjectAlternativeId) pair.
    /// Zero/negative ProjectFileId values are ignored. Alternative ids ≤ 0 are
    /// normalized to null (default alternative).
    /// </summary>
    public static bool HasDuplicateFilingTargets(
        IEnumerable<(int ProjectFileId, int? ProjectAlternativeId)> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        return targets
            .Where(t => t.ProjectFileId > 0)
            .Select(t => (
                t.ProjectFileId,
                ProjectAlternativeId: t.ProjectAlternativeId is > 0 ? t.ProjectAlternativeId : null))
            .GroupBy(t => t)
            .Any(g => g.Count() > 1);
    }
}
