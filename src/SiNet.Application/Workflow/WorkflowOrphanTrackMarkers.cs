namespace SiNet.Application.Workflow;

/// <summary>
/// Canonical Notes marker for workflow instances whose JobType was removed from the project
/// (DEV-011). Prefer this over schema changes; Ops filters on the prefix.
/// </summary>
public static class WorkflowOrphanTrackMarkers
{
    public const string NotesPrefix = "[ORPHAN-TRACK]";

    public const int NotesMaxLength = 2000;

    public static bool IsMarked(string? notes) =>
        !string.IsNullOrWhiteSpace(notes)
        && notes.Contains(NotesPrefix, StringComparison.Ordinal);

    /// <summary>Prepends the orphan marker once; keeps prior notes when present.</summary>
    public static string PrependMarker(string? existingNotes, int jobTypeId, DateTime utcNow)
    {
        if (IsMarked(existingNotes))
            return existingNotes!.Length <= NotesMaxLength
                ? existingNotes
                : existingNotes[..NotesMaxLength];

        var marker = $"{NotesPrefix} JobTypeId={jobTypeId} removed {utcNow:yyyy-MM-dd}Z";
        var combined = string.IsNullOrWhiteSpace(existingNotes)
            ? marker
            : marker + " | " + existingNotes.Trim();
        return combined.Length <= NotesMaxLength ? combined : combined[..NotesMaxLength];
    }
}
