using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>Builds display groups from mailbox rows, project group, and label metadata (DEV-017 exclusive buckets).</summary>
internal static class EmailListGroupBuilder
{
    internal const string SyntheticLabelPrefix = "synthetic:";
    internal const string UnfiledGroupId = SyntheticLabelPrefix + "unfiled";
    internal const string UnfiledDisplayName = "לא מתויג";
    internal const string PersonalDisplayName = "אישי";
    internal const string IrrelevantDisplayName = "לא רלוונטי";

    internal static bool IsSyntheticLabelId(string labelId) =>
        labelId.StartsWith(SyntheticLabelPrefix, StringComparison.Ordinal);

    internal static string ToSyntheticLabelId(string labelName) =>
        SyntheticLabelPrefix + labelName;

    internal sealed record RebuildInput(
        IReadOnlyList<EmailListRow> MailboxRows,
        IReadOnlyList<GmailLabelInfo> AvailableLabels,
        EmailLabelGroupViewModel? ProjectGroup,
        string? ProjectLabelName,
        bool GroupByLabel,
        IReadOnlyDictionary<string, bool> ExpandedByLabelId);

    internal sealed record RebuildResult(
        IReadOnlyList<EmailLabelGroupViewModel> DisplayGroups,
        IReadOnlyList<EmailListRow> FlatDisplayRows,
        bool HasLabelGroups);

    internal static RebuildResult Rebuild(
        RebuildInput input,
        Func<string, string, EmailLabelGroupViewModel> createLabelGroup)
    {
        // Project group is loaded via a dedicated gateway page — preserve those rows and
        // exclude their ids from mailbox flat/other buckets (merge extras that match selected).
        var excludedIds = input.ProjectGroup?.SeenMessageIds ?? [];
        var mailboxRows = input.MailboxRows
            .Where(row => !excludedIds.Contains(row.Id))
            .ToList();
        var flatDisplayRows = mailboxRows;

        if (!input.GroupByLabel)
        {
            var groups = new List<EmailLabelGroupViewModel>();
            if (input.ProjectGroup is not null)
            {
                groups.Add(input.ProjectGroup);
            }

            return new RebuildResult(groups, flatDisplayRows, HasLabelGroups: false);
        }

        var labelIdByName = input.AvailableLabels
            .Where(static label => !string.IsNullOrWhiteSpace(label.Name) && !string.IsNullOrWhiteSpace(label.Id))
            .GroupBy(static label => label.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var otherProjectGroups = new Dictionary<string, EmailLabelGroupViewModel>(StringComparer.Ordinal);
        EmailLabelGroupViewModel? unfiledGroup = null;
        EmailLabelGroupViewModel? irrelevantGroup = null;
        EmailLabelGroupViewModel? personalGroup = null;

        EmailLabelGroupViewModel GetOrCreate(
            string labelId,
            string displayName,
            Dictionary<string, EmailLabelGroupViewModel>? into = null)
        {
            if (into is not null && into.TryGetValue(labelId, out var existing))
            {
                return existing;
            }

            var group = createLabelGroup(labelId, displayName);
            if (input.ExpandedByLabelId.TryGetValue(labelId, out var isExpanded))
            {
                group.IsExpanded = isExpanded;
            }

            into?.Add(labelId, group);
            return group;
        }

        foreach (var row in mailboxRows)
        {
            var bucket = ResolveExclusiveBucket(row, input.ProjectLabelName);
            switch (bucket.Kind)
            {
                case ExclusiveBucketKind.Personal:
                    personalGroup ??= GetOrCreate(
                        ResolveLabelId(EmailGmailLabelNames.Personal, labelIdByName),
                        PersonalDisplayName);
                    personalGroup.TryAddEmail(row);
                    break;

                case ExclusiveBucketKind.Irrelevant:
                    irrelevantGroup ??= GetOrCreate(
                        ResolveLabelId(EmailGmailLabelNames.Irrelevant, labelIdByName),
                        IrrelevantDisplayName);
                    irrelevantGroup.TryAddEmail(row);
                    break;

                case ExclusiveBucketKind.SelectedProject:
                    if (input.ProjectGroup is not null)
                    {
                        input.ProjectGroup.TryAddEmail(row);
                    }
                    else
                    {
                        var selectedLabel = bucket.LabelName ?? input.ProjectLabelName ?? UnfiledDisplayName;
                        var selectedId = ResolveLabelId(selectedLabel, labelIdByName);
                        var fallback = GetOrCreate(
                            selectedId,
                            ToGroupHeaderDisplayName(selectedLabel),
                            otherProjectGroups);
                        fallback.TryAddEmail(row);
                    }

                    break;

                case ExclusiveBucketKind.OtherProject:
                    var projectLabel = bucket.LabelName!;
                    var projectLabelId = ResolveLabelId(projectLabel, labelIdByName);
                    var projectGroup = GetOrCreate(
                        projectLabelId,
                        ToGroupHeaderDisplayName(projectLabel),
                        otherProjectGroups);
                    projectGroup.TryAddEmail(row);
                    break;

                default:
                    unfiledGroup ??= GetOrCreate(UnfiledGroupId, UnfiledDisplayName);
                    unfiledGroup.TryAddEmail(row);
                    break;
            }
        }

        var displayGroups = new List<EmailLabelGroupViewModel>();
        if (input.ProjectGroup is not null)
        {
            displayGroups.Add(input.ProjectGroup);
        }

        foreach (var group in otherProjectGroups.Values
                     .OrderBy(static g => g.LabelDisplayName, StringComparer.OrdinalIgnoreCase))
        {
            group.ResetPagingState();
            displayGroups.Add(group);
        }

        if (unfiledGroup is not null)
        {
            unfiledGroup.ResetPagingState();
            displayGroups.Add(unfiledGroup);
        }

        if (irrelevantGroup is not null)
        {
            irrelevantGroup.ResetPagingState();
            displayGroups.Add(irrelevantGroup);
        }

        if (personalGroup is not null)
        {
            personalGroup.ResetPagingState();
            displayGroups.Add(personalGroup);
        }

        var hasLabelGroups = displayGroups.Count > 0;
        return new RebuildResult(displayGroups, flatDisplayRows, hasLabelGroups);
    }

    internal enum ExclusiveBucketKind
    {
        Personal,
        Irrelevant,
        SelectedProject,
        OtherProject,
        Unfiled,
    }

    internal readonly record struct ExclusiveBucket(ExclusiveBucketKind Kind, string? LabelName = null);

    /// <summary>DEV-017: one message → one bucket.</summary>
    internal static ExclusiveBucket ResolveExclusiveBucket(EmailListRow row, string? selectedProjectLabelName)
    {
        var labelNames = row.LabelChipNames is { Count: > 0 }
            ? row.LabelChipNames
            : string.IsNullOrWhiteSpace(row.PrimaryLabel)
                ? Array.Empty<string>()
                : [row.PrimaryLabel];

        if (labelNames.Any(static n =>
                string.Equals(n, EmailGmailLabelNames.Personal, StringComparison.OrdinalIgnoreCase)))
        {
            return new ExclusiveBucket(ExclusiveBucketKind.Personal);
        }

        if (labelNames.Any(static n =>
                string.Equals(n, EmailGmailLabelNames.Irrelevant, StringComparison.OrdinalIgnoreCase)))
        {
            return new ExclusiveBucket(ExclusiveBucketKind.Irrelevant);
        }

        // Selected project may be stored as leaf ("1042 — Name") or full Gmail path — match either
        // before requiring IsProjectLabel (tests/hosts often use leaf-only ProjectLabelName).
        if (!string.IsNullOrWhiteSpace(selectedProjectLabelName))
        {
            var selected = selectedProjectLabelName.Trim();
            var selectedLeaf = ToGroupHeaderDisplayName(selected);
            var selectedMatch = labelNames.FirstOrDefault(n =>
                !string.IsNullOrWhiteSpace(n)
                && (string.Equals(n, selected, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n, selectedLeaf, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ToGroupHeaderDisplayName(n), selected, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ToGroupHeaderDisplayName(n), selectedLeaf, StringComparison.OrdinalIgnoreCase)));
            if (selectedMatch is not null)
            {
                return new ExclusiveBucket(ExclusiveBucketKind.SelectedProject, selectedMatch);
            }
        }

        var projectLabels = labelNames
            .Where(static n => !string.IsNullOrWhiteSpace(n) && EmailGmailLabelNames.IsProjectLabel(n))
            .ToList();

        if (projectLabels.Count > 0)
        {
            return new ExclusiveBucket(ExclusiveBucketKind.OtherProject, projectLabels[0]);
        }

        return new ExclusiveBucket(ExclusiveBucketKind.Unfiled);
    }

    private static string ResolveLabelId(string labelName, IReadOnlyDictionary<string, string> labelIdByName) =>
        labelIdByName.TryGetValue(labelName, out var resolvedId) && !string.IsNullOrWhiteSpace(resolvedId)
            ? resolvedId
            : ToSyntheticLabelId(labelName);

    /// <summary>
    /// Project labels are Office/City/Project — group headers show the leaf (project) segment only.
    /// Group identity remains the Gmail label id.
    /// </summary>
    internal static string ToGroupHeaderDisplayName(string labelName)
    {
        if (string.IsNullOrWhiteSpace(labelName) || !EmailGmailLabelNames.IsProjectLabel(labelName))
        {
            return labelName;
        }

        return EmailProjectLabelParser.TryExtractProjectDisplaySegment(labelName) ?? labelName;
    }
}
