namespace SiNetSQL.Services.InspectionSync;

/// <summary>
/// Pre-sync validation engine for inspection template tags.
/// Enforces pairing, uniqueness, and chapter-title consistency rules.
/// </summary>
public static class TemplateTagValidator
{
    /// <summary>
    /// The mandatory tag label that defines the planner-response column in the inspection
    /// template. Replaces the legacy "two columns to the right of the note" assumption.
    /// </summary>
    public const string PlannerResponseTagLabel = "תגובת המתכנן";

    /// <summary>
    /// Hebrew labels used in the central validation/UX messages.
    /// </summary>
    public const string PlannerResponseTagMissingMessage =
        "לא נמצאה עמודת תגובת המתכנן בתבנית";
    public const string PlannerResponseTagDuplicateMessage =
        "תגית עמודת תגובת המתכנן יכולה להופיע רק פעם אחת בתבנית";

    /// <summary>
    /// Validates the scanned tags and returns a list of errors.
    /// An empty list means the template is valid and sync may proceed.
    /// </summary>
    public static List<TemplateValidationError> Validate(IReadOnlyList<TemplateScanTag> tags)
    {
        var errors = new List<TemplateValidationError>();

        // ── Rule 0: Mandatory planner-response column tag <<תגובת המתכנן>> ──
        // Must appear exactly once. The cell that contains it determines the column
        // used for writing/reading planner responses on the exported sheet.
        var plannerResponseTags = tags
            .Where(t => t.IsPlannerResponseColumnTag
                || (t.IsGeneralTag
                    && string.Equals(
                        t.GeneralTagLabel?.Trim(),
                        PlannerResponseTagLabel,
                        StringComparison.Ordinal)))
            .ToList();

        if (plannerResponseTags.Count == 0)
        {
            errors.Add(new TemplateValidationError(
                "MISSING_PLANNER_RESPONSE_TAG",
                PlannerResponseTagMissingMessage));
        }
        else if (plannerResponseTags.Count > 1)
        {
            errors.Add(new TemplateValidationError(
                "DUPLICATE_PLANNER_RESPONSE_TAG",
                PlannerResponseTagDuplicateMessage));
        }

        // Only validate numbered (non-general) tags
        var numberedTags = tags.Where(t => !t.IsGeneralTag).ToList();

        // Group by SectionCode
        var byCode = numberedTags
            .GroupBy(t => t.SectionCode, StringComparer.Ordinal)
            .ToList();

        foreach (var group in byCode)
        {
            var code = group.Key;
            var items = group.ToList();

            // ── Rule 4: No more than 2 occurrences per ID ──
            if (items.Count > 2)
            {
                errors.Add(new TemplateValidationError(
                    "EXCESS_DUPLICATE",
                    $"מזהה '{code}' מופיע {items.Count} פעמים — מותר מקסימום 2 (הגדרה + קלט).",
                    code));
            }

            // ── Rule 2: Only one header tag (with brackets) per ID ──
            var headerCount = items.Count(t => t.IsStatusTag);
            if (headerCount > 1)
            {
                errors.Add(new TemplateValidationError(
                    "DUPLICATE_DEFINITION",
                    $"מזהה '{code}' מכיל {headerCount} תגי הגדרה (עם סוגריים מרובעים) — מותר אחד בלבד.",
                    code));
            }

            // ── Rule 3: Pairing — each ID must have both header AND note input ($) ──
            var hasHeader = items.Any(t => t.IsStatusTag);
            var hasNoteInput = items.Any(t => t.IsNoteInputTag);

            if (hasHeader && !hasNoteInput)
            {
                errors.Add(new TemplateValidationError(
                    "MISSING_NOTE_INPUT",
                    $"מזהה '{code}' חסר תג קלט (<<{code} $>>) — נדרש זוג הגדרה + קלט.",
                    code));
            }
            else if (!hasHeader && hasNoteInput)
            {
                errors.Add(new TemplateValidationError(
                    "MISSING_HEADER",
                    $"מזהה '{code}' חסר תג הגדרה (<<{code} כותרת [תוכן]>>) — נדרש זוג הגדרה + קלט.",
                    code));
            }
            else if (!hasHeader && !hasNoteInput)
            {
                // Legacy note tag only (<<X.Y Title>> without brackets or $) —
                // neither header nor note-input was found, section would be lost without fallback.
                errors.Add(new TemplateValidationError(
                    "MISSING_HEADER_AND_INPUT",
                    $"מזהה '{code}' מכיל רק תג הערה ישן (<<{code} כותרת>>) — חסרים גם תג הגדרה וגם תג קלט.",
                    code));
            }
        }

        // ── Rule 1: Chapter title consistency ──
        // All tags with the same major number (e.g. 5.X) must share the same chapter title.
        var headersByChapter = numberedTags
            .Where(t => t.IsStatusTag && !string.IsNullOrWhiteSpace(t.Title))
            .GroupBy(t =>
            {
                var dotIdx = t.SectionCode.IndexOf('.');
                return dotIdx > 0 ? t.SectionCode[..dotIdx] : t.SectionCode;
            });

        foreach (var chapterGroup in headersByChapter)
        {
            var chapterNum = chapterGroup.Key;
            var distinctTitles = chapterGroup
                .Select(t => t.Title!.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (distinctTitles.Count > 1)
            {
                errors.Add(new TemplateValidationError(
                    "CHAPTER_TITLE_MISMATCH",
                    $"תגים בפרק {chapterNum} מכילים כותרות שונות: {string.Join(", ", distinctTitles.Select(t => $"'{t}'"))}.",
                    chapterNum));
            }
        }

        return errors;
    }
}
