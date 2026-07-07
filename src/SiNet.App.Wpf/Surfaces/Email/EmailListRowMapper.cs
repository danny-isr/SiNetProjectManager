using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNet.Application.Projects;
using SiNet.Domain.ValueObjects;

namespace SiNet.App.Wpf.Surfaces.Email;

internal static class EmailListRowMapper
{
    public static async Task<(IReadOnlyList<EmailListRow> Rows, string? EnrichmentWarning)> MapSummariesAsync(
        IReadOnlyList<EmailSummary> summaries,
        IEmailThreadLinkQueryService? threadLinkQuery,
        Func<ProjectSummaryDto?> getCurrentProject)
    {
        if (summaries.Count == 0)
        {
            return ([], null);
        }

        IReadOnlyDictionary<string, EmailProjectLinkInfo> linkStates =
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);
        string? enrichmentWarning = null;

        if (threadLinkQuery is not null)
        {
            var ids = summaries
                .Select(static summary => summary.InternetMessageId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .ToList();

            if (ids.Count > 0)
            {
                try
                {
                    linkStates = await threadLinkQuery
                        .GetLinkStatesByInternetMessageIdsAsync(ids)
                        .ConfigureAwait(true);
                }
                catch
                {
                    enrichmentWarning = "הודעות נטענו, אך מצב שיוך לפרויקט לא נטען.";
                }
            }
        }

        var rows = new List<EmailListRow>(summaries.Count);
        foreach (var summary in summaries)
        {
            rows.Add(ToEmailListRow(summary, linkStates, getCurrentProject));
        }

        return (rows, enrichmentWarning);
    }

    public static EmailListRow ToEmailListRow(
        EmailSummary summary,
        IReadOnlyDictionary<string, EmailProjectLinkInfo> linkStates,
        Func<ProjectSummaryDto?> getCurrentProject)
    {
        EmailProjectLinkInfo? link = null;
        if (!string.IsNullOrWhiteSpace(summary.InternetMessageId))
        {
            var key = summary.InternetMessageId.Trim().Trim('<', '>');
            linkStates.TryGetValue(key, out link);
            if (link is null)
            {
                linkStates.TryGetValue(summary.InternetMessageId, out link);
            }
        }

        var isLinkedFromLabels = InferLinkedFromLabels(summary.LabelNames);
        var isLinked = link?.IsLinked == true || isLinkedFromLabels;
        var projectDisplay = isLinked
            ? link?.DisplayName ?? summary.PrimaryLabel ?? "משויך"
            : "לא משויך";

        var labelChipNames = FilterDisplayLabels(summary.LabelNames);
        var labelChips = FilterDisplayLabelChips(null, summary.LabelNames);
        var labelsDisplay = labelChipNames.Count > 0
            ? string.Join(", ", labelChipNames)
            : string.Empty;
        var isFiledToProject = IsFiledToProject(summary.LabelNames);
        var filedProjectLabelPath = EmailGmailLabelNames.FindProjectLabelPath(summary.LabelNames);
        var isFiledToSameProject = IsFiledToSameProjectForMapping(
            isFiledToProject,
            link?.ProjectId,
            filedProjectLabelPath,
            getCurrentProject);

        return new EmailListRow(
            Id: summary.MessageId,
            Sender: summary.From.Value,
            Subject: string.IsNullOrWhiteSpace(summary.Subject) ? "(ללא נושא)" : summary.Subject,
            Preview: string.IsNullOrWhiteSpace(summary.Snippet)
                ? (summary.HasAttachments ? "יש קבצים מצורפים" : string.Empty)
                : summary.Snippet,
            ReceivedOn: summary.ReceivedAt == DateTimeOffset.MinValue ? DateTime.MinValue : summary.ReceivedAt.LocalDateTime,
            GroupName: summary.PrimaryLabel ?? "ללא label",
            IsUnread: summary.IsUnread,
            IsAssigned: isLinked,
            AssignedProjectName: isLinked ? projectDisplay : null,
            AttachmentCount: summary.AttachmentCount,
            InternetMessageId: summary.InternetMessageId,
            To: summary.To?.Value ?? string.Empty,
            Snippet: summary.Snippet ?? string.Empty,
            LabelsDisplay: labelsDisplay,
            PrimaryLabel: summary.PrimaryLabel ?? "ללא label",
            ProjectLinkState: isLinked ? EmailProjectLinkState.Linked : EmailProjectLinkState.Unlinked,
            ProjectId: link?.ProjectId,
            ProjectNumber: link?.ProjectNumber,
            ProjectName: link?.ProjectName,
            ProjectDisplay: projectDisplay,
            LabelChipNames: labelChipNames,
            LabelChips: labelChips,
            ThreadId: summary.ThreadId,
            InboxMessageId: link?.InboxMessageId,
            ThreadUniqueId: link?.ThreadUniqueId,
            IsFiledToProject: isFiledToProject,
            IsFiledToSameProject: isFiledToSameProject,
            FiledProjectLabelPath: filedProjectLabelPath,
            RowBackgroundColor: ResolveRowBackgroundColor(summary.LabelNames, isFiledToProject, link?.ProjectId, getCurrentProject));
    }

    public static bool IsFiledToSameProjectForMapping(
        bool isFiledToProject,
        int? linkedProjectId,
        string? filedProjectLabelPath,
        Func<ProjectSummaryDto?> getCurrentProject)
    {
        if (!isFiledToProject)
        {
            return false;
        }

        var current = getCurrentProject();
        if (current is null)
        {
            return false;
        }

        if (linkedProjectId == current.ProjectId)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(current.ProjectLabelName) || string.IsNullOrWhiteSpace(filedProjectLabelPath))
        {
            return false;
        }

        return filedProjectLabelPath.EndsWith(
            "/" + current.ProjectLabelName,
            StringComparison.OrdinalIgnoreCase)
            || filedProjectLabelPath.EndsWith(
                current.ProjectLabelName,
                StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveRowBackgroundColor(
        IReadOnlyList<string>? labelNames,
        bool isFiledToProject,
        int? linkedProjectId,
        Func<ProjectSummaryDto?> getCurrentProject)
    {
        if (labelNames is not null)
        {
            if (labelNames.Any(static label =>
                    label.Equals(EmailGmailLabelNames.Pending, StringComparison.OrdinalIgnoreCase)))
            {
                return "#F3E5F5";
            }

            if (labelNames.Any(static label =>
                    label.Equals(EmailGmailLabelNames.Personal, StringComparison.OrdinalIgnoreCase)
                    || label.Equals(EmailGmailLabelNames.Irrelevant, StringComparison.OrdinalIgnoreCase)))
            {
                return "#E3F2FD";
            }
        }

        if (!isFiledToProject)
        {
            return null;
        }

        var currentProjectId = getCurrentProject()?.ProjectId;
        if (currentProjectId.HasValue && linkedProjectId == currentProjectId)
        {
            return "#C8E6C9";
        }

        return "#E0E0E0";
    }

    public static EmailListRow ApplyLabelDisplayFields(
        EmailListRow row,
        IReadOnlyList<string> labelChipNames)
    {
        var displayLabelNames = FilterDisplayLabels(labelChipNames);
        var labelChips = FilterDisplayLabelChips(null, labelChipNames);
        var labelsDisplay = displayLabelNames.Count > 0
            ? string.Join(", ", displayLabelNames)
            : string.Empty;
        var primaryLabel = displayLabelNames.FirstOrDefault() ?? "ללא label";

        return row with
        {
            LabelChipNames = labelChipNames,
            LabelChips = labelChips,
            LabelsDisplay = labelsDisplay,
            PrimaryLabel = primaryLabel,
            GroupName = primaryLabel,
        };
    }

    public static string? ResolveOptimisticRowBackground(
        IReadOnlyList<string> labelChipNames,
        bool isFiledToProject,
        int? linkedProjectId)
    {
        if (labelChipNames.Any(static label =>
                label.Equals(EmailGmailLabelNames.Pending, StringComparison.OrdinalIgnoreCase)))
        {
            return "#F3E5F5";
        }

        if (labelChipNames.Any(static label =>
                label.Equals(EmailGmailLabelNames.Personal, StringComparison.OrdinalIgnoreCase)
                || label.Equals(EmailGmailLabelNames.Irrelevant, StringComparison.OrdinalIgnoreCase)))
        {
            return "#E3F2FD";
        }

        if (!isFiledToProject)
        {
            return null;
        }

        return linkedProjectId.HasValue ? "#C8E6C9" : "#E0E0E0";
    }

    public static EmailListRow BuildOptimisticFiledRow(EmailListRow row, ProjectSummaryDto project)
    {
        var location = project.PlaceName ?? string.Empty;
        var projectLabelPath = $"{EmailGmailLabelNames.RootLabel}/{location}/{project.ProjectNumber} — {project.ProjectName}";
        var labelChipNames = row.LabelChipNames?.ToList() ?? [];
        if (!labelChipNames.Contains(projectLabelPath, StringComparer.OrdinalIgnoreCase))
        {
            labelChipNames.Add(projectLabelPath);
        }

        var filed = row with
        {
            IsFiledToProject = true,
            IsFiledToSameProject = true,
            IsAssigned = true,
            ProjectLinkState = EmailProjectLinkState.Linked,
            ProjectId = project.ProjectId,
            ProjectNumber = project.ProjectNumber,
            ProjectName = project.ProjectName,
            ProjectDisplay = $"{project.ProjectNumber} — {project.ProjectName}",
            AssignedProjectName = $"{project.ProjectNumber} — {project.ProjectName}",
            FiledProjectLabelPath = projectLabelPath,
            RowBackgroundColor = "#C8E6C9",
        };

        return ApplyLabelDisplayFields(filed, labelChipNames);
    }

    public static EmailListRow BuildOptimisticUnfiledRow(EmailListRow row)
    {
        var labelChipNames = row.LabelChipNames?
            .Where(static label => !EmailGmailLabelNames.IsProjectLabel(label))
            .ToList() ?? [];

        var unfiled = row with
        {
            IsFiledToProject = false,
            IsFiledToSameProject = false,
            IsAssigned = false,
            ProjectLinkState = EmailProjectLinkState.Unlinked,
            ProjectId = null,
            ProjectNumber = null,
            ProjectName = null,
            ProjectDisplay = "לא משויך",
            AssignedProjectName = null,
            FiledProjectLabelPath = null,
            RowBackgroundColor = ResolveOptimisticRowBackground(labelChipNames, isFiledToProject: false, linkedProjectId: null),
        };

        return ApplyLabelDisplayFields(unfiled, labelChipNames);
    }

    public static EmailListRow BuildOptimisticStatusRow(EmailListRow row, EmailTriageStatus status)
    {
        var statusLabel = status switch
        {
            EmailTriageStatus.Pending => EmailGmailLabelNames.Pending,
            EmailTriageStatus.Personal => EmailGmailLabelNames.Personal,
            EmailTriageStatus.Irrelevant => EmailGmailLabelNames.Irrelevant,
            _ => null,
        };

        var labelChipNames = row.LabelChipNames?.ToList() ?? [];
        if (statusLabel is not null && !labelChipNames.Any(l => string.Equals(l, statusLabel, StringComparison.OrdinalIgnoreCase)))
        {
            labelChipNames.Add(statusLabel);
        }

        var background = status switch
        {
            EmailTriageStatus.Pending => "#F3E5F5",
            EmailTriageStatus.Personal or EmailTriageStatus.Irrelevant => "#E3F2FD",
            _ => row.RowBackgroundColor,
        };

        var updated = row with { RowBackgroundColor = background };
        return ApplyLabelDisplayFields(updated, labelChipNames);
    }

    public static IReadOnlyList<EmailLabelChip> FilterDisplayLabelChips(
        IReadOnlyList<EmailLabelChip>? labelChips,
        IReadOnlyList<string>? labelNames)
    {
        if (labelChips is { Count: > 0 })
        {
            return labelChips
                .Where(static chip => !IsSystemGmailLabel(chip.DisplayName))
                .DistinctBy(static chip => chip.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return FilterDisplayLabels(labelNames)
            .Select(static name => new EmailLabelChip(name))
            .ToList();
    }

    public static IReadOnlyList<string> FilterDisplayLabels(IReadOnlyList<string>? labelNames)
    {
        if (labelNames is null || labelNames.Count == 0)
        {
            return [];
        }

        return labelNames
            .Where(static label => !IsSystemGmailLabel(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsFiledToProject(IReadOnlyList<string>? labelNames) =>
        labelNames?.Any(static label => EmailGmailLabelNames.IsProjectLabel(label)) == true;

    private static bool IsSystemGmailLabel(string label) =>
        label.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
        || label.Equals("UNREAD", StringComparison.OrdinalIgnoreCase)
        || label.Equals("SENT", StringComparison.OrdinalIgnoreCase)
        || label.Equals("DRAFT", StringComparison.OrdinalIgnoreCase)
        || label.Equals("SPAM", StringComparison.OrdinalIgnoreCase)
        || label.Equals("TRASH", StringComparison.OrdinalIgnoreCase)
        || label.Equals("STARRED", StringComparison.OrdinalIgnoreCase)
        || label.Equals("IMPORTANT", StringComparison.OrdinalIgnoreCase)
        || label.StartsWith("CATEGORY_", StringComparison.OrdinalIgnoreCase);

    private static bool InferLinkedFromLabels(IReadOnlyList<string>? labelNames)
    {
        if (labelNames is null || labelNames.Count == 0)
        {
            return false;
        }

        return labelNames.Any(static label => EmailGmailLabelNames.IsProjectLabel(label));
    }
}
