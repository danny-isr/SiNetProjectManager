using SiNet.Application.Abstractions.Email;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>Builds display groups from mailbox rows, project group, and label metadata.</summary>
internal static class EmailListGroupBuilder
{
    internal const string SyntheticLabelPrefix = "synthetic:";

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

        var groupsById = new Dictionary<string, EmailLabelGroupViewModel>(StringComparer.Ordinal);
        var mergedProjectLabelId = TryResolveProjectLabelId(input.ProjectLabelName, labelIdByName);

        foreach (var row in mailboxRows)
        {
            var labelNames = row.LabelChipNames is { Count: > 0 }
                ? row.LabelChipNames
                : [row.PrimaryLabel ?? "ללא label"];

            foreach (var labelName in labelNames)
            {
                if (string.IsNullOrWhiteSpace(labelName))
                {
                    continue;
                }

                var labelId = labelIdByName.TryGetValue(labelName, out var resolvedId) && !string.IsNullOrWhiteSpace(resolvedId)
                    ? resolvedId
                    : ToSyntheticLabelId(labelName);

                if (mergedProjectLabelId is not null
                    && string.Equals(labelId, mergedProjectLabelId, StringComparison.Ordinal))
                {
                    input.ProjectGroup?.TryAddEmail(row);
                    continue;
                }

                if (!groupsById.TryGetValue(labelId, out var group))
                {
                    group = createLabelGroup(labelId, labelName);
                    if (input.ExpandedByLabelId.TryGetValue(labelId, out var isExpanded))
                    {
                        group.IsExpanded = isExpanded;
                    }

                    groupsById[labelId] = group;
                }

                group.TryAddEmail(row);
            }
        }

        var displayGroups = new List<EmailLabelGroupViewModel>();
        if (input.ProjectGroup is not null)
        {
            displayGroups.Add(input.ProjectGroup);
        }

        foreach (var group in groupsById.Values.OrderBy(static g => g.LabelDisplayName, StringComparer.OrdinalIgnoreCase))
        {
            group.ResetPagingState();
            displayGroups.Add(group);
        }

        var hasLabelGroups = groupsById.Count > 0 || input.ProjectGroup is not null;
        return new RebuildResult(displayGroups, flatDisplayRows, hasLabelGroups);
    }

    private static string? TryResolveProjectLabelId(
        string? projectLabelName,
        IReadOnlyDictionary<string, string> labelIdByName)
    {
        if (string.IsNullOrWhiteSpace(projectLabelName))
        {
            return null;
        }

        return labelIdByName.TryGetValue(projectLabelName.Trim(), out var labelId) ? labelId : null;
    }
}
