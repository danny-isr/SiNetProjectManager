using SiNet.Application.Abstractions.Email;
using SiNet.Application.Projects;

namespace SiNet.Application.Email;

/// <summary>
/// Pure label→project classification for the mailbox audit table (DEV-026). No Gmail I/O.
/// </summary>
public static class GmailMailboxLabelAuditMatcher
{
    public static IReadOnlyList<GmailMailboxLabelAuditRow> BuildRows(
        IReadOnlyList<GmailLabelInfo> labels,
        IReadOnlyList<ProjectSummaryDto> projects,
        IReadOnlyList<string>? placeTitles = null,
        string rootLabel = EmailGmailLabelNames.RootLabel)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(projects);

        var projectsByNumber = new Dictionary<int, ProjectSummaryDto>();
        foreach (var project in projects)
        {
            if (!int.TryParse(project.ProjectNumber, out var number) || number <= 0)
            {
                continue;
            }

            projectsByNumber.TryAdd(number, project);
        }

        var catalogTitles = placeTitles?
            .Where(static t => !string.IsNullOrWhiteSpace(t))
            .Select(static t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

        var drafts = new List<DraftRow>();
        foreach (var label in labels)
        {
            if (string.IsNullOrWhiteSpace(label.Name)
                || GmailSystemLabelNames.IsSystemLabel(label.Name))
            {
                continue;
            }

            drafts.Add(MapDraft(label, projectsByNumber, catalogTitles, rootLabel));
        }

        var duplicateNumbers = drafts
            .Where(static d => d.ParsedProjectNumber is int n && n > 0 && d.MatchedProject)
            .GroupBy(static d => d.ParsedProjectNumber!.Value)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToHashSet();

        var rows = new List<GmailMailboxLabelAuditRow>(drafts.Count);
        foreach (var draft in drafts)
        {
            var isDuplicate = draft.ParsedProjectNumber is int number
                && duplicateNumbers.Contains(number);
            var note = BuildNote(draft, drafts, isDuplicate);
            rows.Add(new GmailMailboxLabelAuditRow(
                draft.LabelId,
                draft.LabelName,
                draft.ParsedProjectNumber,
                draft.ProjectDisplayName,
                draft.PlaceName,
                note,
                isDuplicate));
        }

        return rows
            .OrderByDescending(static r => r.IsDuplicate)
            .ThenBy(static r => r.ParsedProjectNumber ?? int.MaxValue)
            .ThenBy(static r => r.LabelName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DraftRow MapDraft(
        GmailLabelInfo label,
        IReadOnlyDictionary<int, ProjectSummaryDto> projectsByNumber,
        IReadOnlyList<string> catalogTitles,
        string rootLabel)
    {
        var name = label.Name.Trim();
        var leaf = EmailProjectLabelParser.TryExtractProjectDisplaySegment(name) ?? name;
        var parsedNumber = EmailProjectLabelParser.TryExtractProjectIdFromDisplaySegment(leaf);
        var placeName = TryExtractPlaceSegment(name, rootLabel);
        var underRoot = name.StartsWith($"{rootLabel}/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, rootLabel, StringComparison.OrdinalIgnoreCase);

        string? projectDisplay = null;
        var matchedProject = false;
        if (parsedNumber is int number && projectsByNumber.TryGetValue(number, out var project))
        {
            matchedProject = true;
            projectDisplay = string.IsNullOrWhiteSpace(project.ProjectLabelName)
                ? $"({project.ProjectNumber}){project.ProjectName}"
                : project.ProjectLabelName;
        }

        string? closePlace = null;
        if (!string.IsNullOrWhiteSpace(placeName) && catalogTitles.Count > 0)
        {
            var exact = catalogTitles.Any(t =>
                string.Equals(
                    HebrewLabelSimilarity.Normalize(t),
                    HebrewLabelSimilarity.Normalize(placeName),
                    StringComparison.Ordinal));
            if (!exact)
            {
                closePlace = catalogTitles.FirstOrDefault(t => HebrewLabelSimilarity.IsClose(placeName, t));
            }
        }

        return new DraftRow(
            string.IsNullOrWhiteSpace(label.Id) ? name : label.Id,
            name,
            parsedNumber,
            projectDisplay,
            placeName,
            matchedProject,
            underRoot,
            closePlace);
    }

    private static string BuildNote(DraftRow draft, IReadOnlyList<DraftRow> all, bool isDuplicate)
    {
        var parts = new List<string>();
        if (isDuplicate && draft.ParsedProjectNumber is int number)
        {
            var others = all
                .Where(d => d.ParsedProjectNumber == number
                    && !string.Equals(d.LabelName, draft.LabelName, StringComparison.Ordinal))
                .Select(static d => d.LabelName)
                .ToArray();
            if (others.Length == 1)
            {
                parts.Add($"כפילות: גם «{others[0]}» משויך לאותו פרויקט");
            }
            else if (others.Length > 1)
            {
                parts.Add("כפילות: גם " + string.Join("; ", others.Select(static o => $"«{o}»")) + " משויכים לאותו פרויקט");
            }
        }

        if (draft.ParsedProjectNumber is not null && !draft.MatchedProject)
        {
            parts.Add("מספר לא במערכת");
        }

        if (draft.ParsedProjectNumber is not null && !draft.UnderRoot)
        {
            parts.Add("(מספר) מחוץ לשורש הפרויקטים");
        }

        if (!string.IsNullOrWhiteSpace(draft.ClosePlaceTitle))
        {
            parts.Add($"תיקיית יישוב קרובה ל-«{draft.ClosePlaceTitle}»");
        }

        return string.Join(" · ", parts);
    }

    internal static string? TryExtractPlaceSegment(string labelName, string rootLabel)
    {
        if (string.IsNullOrWhiteSpace(labelName)
            || !labelName.StartsWith($"{rootLabel}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var suffix = labelName[(rootLabel.Length + 1)..];
        var slash = suffix.IndexOf('/');
        var place = slash < 0 ? suffix : suffix[..slash];
        return string.IsNullOrWhiteSpace(place) ? null : place.Trim();
    }

    private sealed record DraftRow(
        string LabelId,
        string LabelName,
        int? ParsedProjectNumber,
        string? ProjectDisplayName,
        string? PlaceName,
        bool MatchedProject,
        bool UnderRoot,
        string? ClosePlaceTitle);
}
