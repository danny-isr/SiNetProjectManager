namespace SiNet.Application.Inspection;

/// <summary>
/// Pure open-target decision for note-linked files (host-neutral port of legacy
/// <c>InspectionFileLinkHelper.DecideOpen</c>).
/// </summary>
public static class InspectionLinkedFileOpenRules
{
    public const string MessageWorkWindowRequiredForOpen =
        "כדי לפתוח קובץ מתוך הדוח, יש לפתוח קודם את חלון העבודה.";

    public const string MessageReviewedVersionRequired =
        "יש לבחור גרסת דוח לפני בחירת התוכנית הנבדקת.";

    public const string MessageMultipleFilesNeedExplicitChoice =
        "להערה אין שיוך פרטני והתוכנית הנבדקת כוללת כמה קבצים. " +
        "בחר שיוך פרטני או בחר קובץ לפתיחה.";

    public static InspectionLinkedFileOpenDecision DecideOpen(
        string? linkedFileName,
        string? linkedAlternative,
        string? linkedVersion,
        string? reviewedVersion,
        IReadOnlyList<(string FileName, string? Alternative)> reviewedFiles,
        bool fileOpenServiceAvailable)
    {
        if (!fileOpenServiceAvailable)
            return InspectionLinkedFileOpenDecision.Disabled(MessageWorkWindowRequiredForOpen);

        if (!string.IsNullOrWhiteSpace(linkedFileName))
        {
            var tip = $"פתח קובץ: {linkedFileName}, " +
                      $"חלופה: {linkedAlternative ?? "—"}, " +
                      $"גרסה: {linkedVersion ?? "—"}";
            return InspectionLinkedFileOpenDecision.Enabled(
                linkedFileName!,
                linkedAlternative,
                linkedVersion,
                tip);
        }

        if (string.IsNullOrWhiteSpace(reviewedVersion))
            return InspectionLinkedFileOpenDecision.Disabled(MessageReviewedVersionRequired);

        if (reviewedFiles.Count != 1)
            return InspectionLinkedFileOpenDecision.Disabled(MessageMultipleFilesNeedExplicitChoice);

        var only = reviewedFiles[0];
        var fallbackTip = $"פתח את התוכנית הנבדקת: {only.FileName}, " +
                          $"חלופה: {only.Alternative ?? "—"}, " +
                          $"גרסה: {reviewedVersion}";
        return InspectionLinkedFileOpenDecision.Enabled(
            only.FileName,
            only.Alternative,
            reviewedVersion,
            fallbackTip);
    }
}

public readonly record struct InspectionLinkedFileOpenDecision(
    bool IsEnabled,
    string? FileName,
    string? Alternative,
    string? Version,
    string Tooltip)
{
    public static InspectionLinkedFileOpenDecision Disabled(string tooltip) =>
        new(false, null, null, null, tooltip);

    public static InspectionLinkedFileOpenDecision Enabled(
        string fileName,
        string? alternative,
        string? version,
        string tooltip) =>
        new(true, fileName, alternative, version, tooltip);
}
