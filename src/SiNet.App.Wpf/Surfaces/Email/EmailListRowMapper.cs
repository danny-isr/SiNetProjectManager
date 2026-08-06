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
        IReadOnlyDictionary<string, EmailProjectLinkInfo> messageLinkStates =
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, EmailProjectLinkInfo> threadLinkStates =
            new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);
        string? enrichmentWarning = null;
        if (threadLinkQuery is not null)
        {
            var ids = summaries
                .Select(static summary => summary.InternetMessageId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .ToList();
            var threadIds = summaries
                .Select(static summary => summary.ThreadId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            try
            {
                Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>>? messageLinkTask = null;
                Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>>? threadLinkTask = null;
                if (ids.Count > 0)
                {
                    messageLinkTask = threadLinkQuery.GetLinkStatesByInternetMessageIdsAsync(ids);
                }
                if (threadIds.Count > 0)
                {
                    threadLinkTask = threadLinkQuery.GetLinkStatesByGmailThreadIdsAsync(threadIds);
                }
                if (messageLinkTask is not null && threadLinkTask is not null)
                {
                    await Task.WhenAll(messageLinkTask, threadLinkTask).ConfigureAwait(true);
                    messageLinkStates = await messageLinkTask.ConfigureAwait(true);
                    threadLinkStates = await threadLinkTask.ConfigureAwait(true);
                }
                else if (messageLinkTask is not null)
                {
                    messageLinkStates = await messageLinkTask.ConfigureAwait(true);
                }
                else if (threadLinkTask is not null)
                {
                    threadLinkStates = await threadLinkTask.ConfigureAwait(true);
                }
            }
            catch
            {
                enrichmentWarning = "הודעות נטענו, אך מצב שיוך לפרויקט לא נטען.";
            }
        }
        var rows = new List<EmailListRow>(summaries.Count);
        foreach (var summary in summaries)
        {
            rows.Add(ToEmailListRow(summary, messageLinkStates, threadLinkStates, getCurrentProject));
        }
        return (rows, enrichmentWarning);
    }
    public static EmailListRow ToEmailListRow(
        EmailSummary summary,
        IReadOnlyDictionary<string, EmailProjectLinkInfo> messageLinkStates,
        IReadOnlyDictionary<string, EmailProjectLinkInfo> threadLinkStates,
        Func<ProjectSummaryDto?> getCurrentProject)
    {
        EmailProjectLinkInfo? messageLink = null;
        if (!string.IsNullOrWhiteSpace(summary.InternetMessageId))
        {
            var key = summary.InternetMessageId.Trim().Trim('<', '>');
            messageLinkStates.TryGetValue(key, out messageLink);
            if (messageLink is null)
            {
                messageLinkStates.TryGetValue(summary.InternetMessageId, out messageLink);
            }
        }
        EmailProjectLinkInfo? threadLink = null;
        if (!string.IsNullOrWhiteSpace(summary.ThreadId))
        {
            threadLinkStates.TryGetValue(summary.ThreadId, out threadLink);
        }
        var filedProjectLabelPath = EmailGmailLabelNames.FindProjectLabelPath(summary.LabelNames);
        var labelProject = EmailProjectLabelParser.TryParseProjectFromLabelPath(filedProjectLabelPath);
        var labelProjectId = labelProject?.ProjectId;
        var labelProjectName = labelProject?.ProjectDisplayName;
        var threadProjectId = threadLink?.ThreadProjectId ?? messageLink?.ThreadProjectId;
        var threadProjectName = threadLink?.ThreadProjectName ?? messageLink?.ThreadProjectName;
        var hasThreadHistory = threadLink?.HasThreadHistory == true || messageLink?.HasThreadHistory == true;
        var isFiledToProject = IsFiledToProject(summary.LabelNames);
        var isProjectMismatch = isFiledToProject
                                && hasThreadHistory
                                && labelProjectId.HasValue
                                && threadProjectId.HasValue
                                && labelProjectId != threadProjectId;
        var alreadyOnThreadProject =
            (labelProjectId.HasValue && labelProjectId == threadProjectId)
            || (messageLink?.InboxProjectId is int inboxPid && inboxPid == threadProjectId);
        var showLinkToThreadButton = hasThreadHistory
                                     && threadProjectId.HasValue
                                     && !alreadyOnThreadProject;
        // List badge «משויך» = Gmail project label only (EMAIL_ACC_SOURCE_OF_TRUTH).
        var isLinked = isFiledToProject;
        var labelLeaf = string.IsNullOrWhiteSpace(filedProjectLabelPath)
            ? null
            : EmailProjectLabelParser.TryExtractProjectDisplaySegment(filedProjectLabelPath);
        var projectDisplay = isLinked
            ? (labelProjectName
               ?? labelLeaf
               ?? messageLink?.DisplayName
               ?? threadLink?.DisplayName
               ?? summary.PrimaryLabel
               ?? "משויך")
            : "לא משויך";
        var labelNumberFromLeaf = labelLeaf is not null
            ? EmailProjectLabelParser.TryExtractProjectIdFromDisplaySegment(labelLeaf)?.ToString()
            : null;
        var labelChipNames = FilterDisplayLabels(summary.LabelNames);
        var labelChips = FilterDisplayLabelChips(null, summary.LabelNames);
        var labelsDisplay = labelChipNames.Count > 0
            ? string.Join(", ", labelChipNames)
            : string.Empty;
        var isFiledToSameProject = IsFiledToSameProjectForMapping(
            isFiledToProject,
            labelProjectId ?? messageLink?.ProjectId ?? threadProjectId,
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
            ProjectId: labelProjectId ?? messageLink?.ProjectId ?? threadProjectId,
            ProjectNumber: labelNumberFromLeaf
                ?? labelProjectId?.ToString()
                ?? (isLinked ? messageLink?.ProjectNumber ?? threadLink?.ProjectNumber : null),
            ProjectName: isLinked
                ? (labelProjectName ?? labelLeaf ?? messageLink?.ProjectName ?? threadLink?.ProjectName)
                : null,
            ProjectDisplay: projectDisplay,
            LabelChipNames: labelChipNames,
            LabelChips: labelChips,
            ThreadId: summary.ThreadId,
            InboxMessageId: messageLink?.InboxMessageId,
            ThreadUniqueId: messageLink?.ThreadUniqueId ?? threadLink?.ThreadUniqueId,
            IsFiledToProject: isFiledToProject,
            IsFiledToSameProject: isFiledToSameProject,
            FiledProjectLabelPath: filedProjectLabelPath,
            RowBackgroundColor: ResolveRowBackgroundColor(
                summary.LabelNames,
                isFiledToProject,
                labelProjectId ?? messageLink?.ProjectId,
                getCurrentProject,
                isProjectMismatch,
                hasThreadHistory),
            LabelProjectId: labelProjectId,
            LabelProjectName: labelProjectName,
            ThreadProjectId: threadProjectId,
            ThreadProjectName: threadProjectName,
            HasThreadHistory: hasThreadHistory,
            IsProjectMismatch: isProjectMismatch,
            ShowLinkToThreadButton: showLinkToThreadButton);
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
        Func<ProjectSummaryDto?> getCurrentProject,
        bool isProjectMismatch = false,
        bool hasThreadHistory = false)
    {
        if (isProjectMismatch)
        {
            return "#FFFFD54F";
        }
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
        if (isFiledToProject)
        {
            var currentProjectId = getCurrentProject()?.ProjectId;
            if (currentProjectId.HasValue && linkedProjectId == currentProjectId)
            {
                return "#C8E6C9";
            }
            return "#E0E0E0";
        }
        if (hasThreadHistory)
        {
            return "#F5F5F5";
        }
        return null;
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
        int? linkedProjectId,
        bool isProjectMismatch = false,
        bool hasThreadHistory = false)
    {
        if (isProjectMismatch)
        {
            return "#FFFFD54F";
        }
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
            return hasThreadHistory ? "#F5F5F5" : null;
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
            LabelProjectId = project.ProjectId,
            LabelProjectName = project.ProjectLabelName ?? $"{project.ProjectNumber} — {project.ProjectName}",
            ThreadProjectId = row.ThreadProjectId ?? project.ProjectId,
            ThreadProjectName = row.ThreadProjectName ?? project.ProjectLabelName ?? $"{project.ProjectNumber} — {project.ProjectName}",
            HasThreadHistory = row.HasThreadHistory || row.ThreadProjectId.HasValue || row.ThreadId is not null,
            IsProjectMismatch = false,
            ShowLinkToThreadButton = false,
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
            IsAssigned = row.HasThreadHistory,
            ProjectLinkState = row.HasThreadHistory ? EmailProjectLinkState.Linked : EmailProjectLinkState.Unlinked,
            ProjectId = row.HasThreadHistory ? row.ThreadProjectId : null,
            ProjectNumber = row.HasThreadHistory ? row.ProjectNumber : null,
            ProjectName = row.HasThreadHistory ? row.ProjectName : null,
            ProjectDisplay = row.HasThreadHistory ? row.ThreadProjectName ?? row.ProjectDisplay : "לא משויך",
            AssignedProjectName = row.HasThreadHistory ? row.ThreadProjectName : null,
            FiledProjectLabelPath = null,
            LabelProjectId = null,
            LabelProjectName = null,
            IsProjectMismatch = false,
            ShowLinkToThreadButton = row.HasThreadHistory && row.ThreadProjectId.HasValue,
            RowBackgroundColor = ResolveOptimisticRowBackground(
                labelChipNames,
                isFiledToProject: false,
                linkedProjectId: row.ThreadProjectId,
                hasThreadHistory: row.HasThreadHistory),
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
            EmailTriageStatus.Fyi => EmailGmailLabelNames.Fyi,
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
            EmailTriageStatus.Fyi => "#E8F5E9",
            _ => row.RowBackgroundColor,
        };
        var clearsUnread = status is EmailTriageStatus.Personal
            or EmailTriageStatus.Irrelevant
            or EmailTriageStatus.Fyi;
        var updated = row with
        {
            RowBackgroundColor = background,
            IsUnread = clearsUnread ? false : row.IsUnread,
        };
        return ApplyLabelDisplayFields(updated, labelChipNames);
    }
    public static IReadOnlyList<EmailLabelChip> FilterDisplayLabelChips(
        IReadOnlyList<EmailLabelChip>? labelChips,
        IReadOnlyList<string>? labelNames)
    {
        if (labelChips is { Count: > 0 })
        {
            return OrderDisplayLabelChips(
                labelChips
                    .Where(static chip => !IsSystemGmailLabel(chip.DisplayName))
                    .DistinctBy(static chip => chip.DisplayName, StringComparer.OrdinalIgnoreCase));
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

        return OrderDisplayLabels(
            labelNames
                .Where(static label => !IsSystemGmailLabel(label))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Project labels first, other user labels next, OfficeSystem / Personal last.</summary>
    public static IReadOnlyList<string> OrderDisplayLabels(IEnumerable<string> labels) =>
        labels
            .OrderBy(static label => LabelDisplaySortKey(label))
            .ThenBy(static label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<EmailLabelChip> OrderDisplayLabelChips(IEnumerable<EmailLabelChip> chips) =>
        chips
            .OrderBy(static chip => LabelDisplaySortKey(chip.DisplayName))
            .ThenBy(static chip => chip.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int LabelDisplaySortKey(string label)
    {
        if (EmailGmailLabelNames.IsProjectLabel(label))
            return 0;
        if (label.StartsWith("OfficeSystem_", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Personal", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
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
}
