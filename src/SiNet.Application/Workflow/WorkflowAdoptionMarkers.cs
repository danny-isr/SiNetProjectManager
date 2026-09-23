namespace SiNet.Application.Workflow;

/// <summary>
/// Notes prefix that marks a workflow started by Adopt Existing Workflow.
/// Same pattern as <see cref="WorkflowOrphanTrackMarkers"/>: no schema change.
/// <see cref="WorkflowTriggerTypeDto.System"/> alone is not enough, because continuation
/// starts also use System.
/// </summary>
public static class WorkflowAdoptionMarkers
{
    public const string NotesPrefix = "[ADOPTED]";

    public const string SourceManual = "Manual";

    public const int NotesMaxLength = 2000;

    public static bool IsMarked(string? notes) =>
        !string.IsNullOrWhiteSpace(notes)
        && notes.StartsWith(NotesPrefix, StringComparison.Ordinal);

    public static string BuildNotes(
        string stageCode,
        string? stageName,
        DateTime? originalStartedAt,
        string? userNotes)
    {
        var text = $"{NotesPrefix} Existing workflow adopted into SiNet at {stageCode}.";
        if (!string.IsNullOrWhiteSpace(stageName))
            text += $" StageName={stageName.Trim()}.";
        text += $" Source={SourceManual}.";
        if (originalStartedAt is DateTime started)
            text += $" OriginalStartedAt={started:yyyy-MM-dd}.";
        if (!string.IsNullOrWhiteSpace(userNotes))
            text += " " + userNotes.Trim();

        return text.Length <= NotesMaxLength ? text : text[..NotesMaxLength];
    }

    public static string TransitionLabel(string? stageName) =>
        $"התהליך הוטמע ב־SiNet בשלב {stageName ?? "הנוכחי"}";
}
