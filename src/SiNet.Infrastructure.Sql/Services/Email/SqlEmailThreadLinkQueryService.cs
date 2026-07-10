using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email;

/// <summary>
/// Read-only project-link enrichment from <c>EmailInboxMessage</c> and <c>ThreadStatusMapping</c>.
/// </summary>
public sealed class SqlEmailThreadLinkQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IEmailThreadLinkQueryService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByInternetMessageIdsAsync(
        IReadOnlyList<string> internetMessageIds,
        CancellationToken cancellationToken = default) =>
        QueryByInternetMessageIdsAsync(internetMessageIds, cancellationToken);

    public Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByGmailThreadIdsAsync(
        IReadOnlyList<string> gmailThreadIds,
        CancellationToken cancellationToken = default) =>
        QueryByGmailThreadIdsAsync(gmailThreadIds, cancellationToken);

    private async Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> QueryByInternetMessageIdsAsync(
        IReadOnlyList<string> internetMessageIds,
        CancellationToken cancellationToken)
    {
        if (internetMessageIds.Count == 0)
        {
            return new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);
        }

        var lookupKeys = internetMessageIds
            .SelectMany(static id => new[] { NormalizeMessageId(id), id.Trim() })
            .Where(static key => key.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (lookupKeys.Count == 0)
        {
            return new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var inboxRows = await db.EmailInboxMessages
            .AsNoTracking()
            .Where(message =>
                lookupKeys.Contains(message.InternetMessageId)
                || lookupKeys.Contains(message.MessageUniqueId))
            .Select(message => new InboxRow(
                message.Id,
                message.InternetMessageId,
                message.MessageUniqueId,
                message.ThreadUniqueId,
                message.GmailThreadId,
                message.ProjectId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (inboxRows.Count == 0)
        {
            return new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);
        }

        var threadMappings = await LoadThreadMappingsAsync(
            db,
            inboxRows.Select(static row => row.ThreadUniqueId).Where(static key => !string.IsNullOrWhiteSpace(key)).Cast<string>(),
            inboxRows.Select(static row => row.GmailThreadId).Where(static key => !string.IsNullOrWhiteSpace(key)).Cast<string>(),
            cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var inbox in inboxRows)
        {
            var mapping = ResolveMappingForInbox(inbox, threadMappings);
            var info = BuildInfoFromInboxAndMapping(inbox, mapping, threadMappings.Projects);

            AddKey(result, inbox.InternetMessageId, info);
            AddKey(result, inbox.MessageUniqueId, info);
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> QueryByGmailThreadIdsAsync(
        IReadOnlyList<string> gmailThreadIds,
        CancellationToken cancellationToken)
    {
        var threadIds = gmailThreadIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (threadIds.Count == 0)
        {
            return new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var gmailToUnique = await ResolveThreadUniqueIdsAsync(db, threadIds, cancellationToken).ConfigureAwait(false);
        var threadMappings = await LoadThreadMappingsAsync(
            db,
            gmailToUnique.Values,
            threadIds,
            cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var threadId in threadIds)
        {
            var mapping = ResolveMappingForGmailThread(threadId, gmailToUnique, threadMappings);
            if (mapping is null || mapping.ProjectId <= 0)
            {
                continue;
            }

            threadMappings.Projects.TryGetValue(mapping.ProjectId, out var project);
            var display = BuildDisplayName(
                project?.NameAndNumber,
                project?.Number,
                project?.Title);

            result[threadId] = new EmailProjectLinkInfo(
                IsLinked: true,
                ProjectId: mapping.ProjectId,
                ProjectNumber: project?.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ProjectName: project?.Title,
                DisplayName: display,
                ThreadUniqueId: mapping.ThreadUniqueId,
                GmailThreadId: threadId,
                ThreadProjectId: mapping.ProjectId,
                ThreadProjectName: display,
                HasThreadHistory: true);
        }

        return result;
    }

    private static async Task<ThreadMappingBundle> LoadThreadMappingsAsync(
        SiNetSQLDbContext db,
        IEnumerable<string> threadUniqueIds,
        IEnumerable<string> gmailThreadIds,
        CancellationToken cancellationToken)
    {
        var uniqueKeys = threadUniqueIds
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var gmailKeys = gmailThreadIds
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mappings = await db.ThreadStatusMappings
            .AsNoTracking()
            .Where(mapping =>
                (uniqueKeys.Count > 0 && uniqueKeys.Contains(mapping.ThreadUniqueId))
                || (gmailKeys.Count > 0 && mapping.ThreadId != null && gmailKeys.Contains(mapping.ThreadId)))
            .Select(mapping => new MappingRow(
                mapping.ThreadUniqueId,
                mapping.ThreadId,
                mapping.ProjectId,
                mapping.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var projectIds = mappings
            .Select(static row => row.ProjectId)
            .Where(static id => id > 0)
            .Distinct()
            .ToList();

        var projects = projectIds.Count == 0
            ? new Dictionary<int, ProjectRow>()
            : await db.Projects
                .AsNoTracking()
                .Where(project => projectIds.Contains(project.Id))
                .Select(project => new ProjectRow(
                    project.Id,
                    project.Number,
                    project.Title,
                    project.NameAndNumber))
                .ToDictionaryAsync(static project => project.Id, cancellationToken)
                .ConfigureAwait(false);

        return new ThreadMappingBundle(mappings, projects);
    }

    private static async Task<Dictionary<string, string>> ResolveThreadUniqueIdsAsync(
        SiNetSQLDbContext db,
        IReadOnlyList<string> gmailThreadIds,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (gmailThreadIds.Count == 0)
        {
            return map;
        }

        var rows = await db.EmailInboxMessages
            .AsNoTracking()
            .Where(message =>
                message.GmailThreadId != null
                && gmailThreadIds.Contains(message.GmailThreadId)
                && message.ThreadUniqueId != null
                && message.ThreadUniqueId != string.Empty)
            .Select(message => new { message.GmailThreadId, message.ThreadUniqueId, message.ReceivedUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var group in rows.GroupBy(static row => row.GmailThreadId!, StringComparer.OrdinalIgnoreCase))
        {
            var latest = group.OrderByDescending(static row => row.ReceivedUtc).First();
            map[group.Key] = latest.ThreadUniqueId!;
        }

        return map;
    }

    private static MappingRow? ResolveMappingForGmailThread(
        string gmailThreadId,
        IReadOnlyDictionary<string, string> gmailToUnique,
        ThreadMappingBundle bundle)
    {
        if (gmailToUnique.TryGetValue(gmailThreadId, out var threadUniqueId))
        {
            var byUnique = bundle.Mappings
                .Where(row => string.Equals(row.ThreadUniqueId, threadUniqueId, StringComparison.Ordinal)
                              && row.ProjectId > 0)
                .OrderByDescending(static row => row.Status == ThreadMappingStatus.Assigned)
                .FirstOrDefault();
            if (byUnique is not null)
            {
                return byUnique;
            }
        }

        return bundle.Mappings
            .Where(row => string.Equals(row.GmailThreadId, gmailThreadId, StringComparison.OrdinalIgnoreCase)
                          && row.ProjectId > 0)
            .OrderByDescending(static row => row.Status == ThreadMappingStatus.Assigned)
            .FirstOrDefault();
    }

    private static MappingRow? ResolveMappingForInbox(InboxRow inbox, ThreadMappingBundle bundle)
    {
        if (!string.IsNullOrWhiteSpace(inbox.ThreadUniqueId))
        {
            var byUnique = bundle.Mappings
                .FirstOrDefault(row => string.Equals(row.ThreadUniqueId, inbox.ThreadUniqueId, StringComparison.Ordinal)
                                       && row.ProjectId > 0);
            if (byUnique is not null)
            {
                return byUnique;
            }
        }

        if (!string.IsNullOrWhiteSpace(inbox.GmailThreadId))
        {
            return bundle.Mappings
                .FirstOrDefault(row => string.Equals(row.GmailThreadId, inbox.GmailThreadId, StringComparison.OrdinalIgnoreCase)
                                       && row.ProjectId > 0);
        }

        return null;
    }

    private static EmailProjectLinkInfo BuildInfoFromInboxAndMapping(
        InboxRow inbox,
        MappingRow? mapping,
        IReadOnlyDictionary<int, ProjectRow> projects)
    {
        var threadProjectId = mapping?.ProjectId;
        projects.TryGetValue(threadProjectId ?? inbox.ProjectId, out var threadProject);
        projects.TryGetValue(inbox.ProjectId, out var inboxProject);

        var hasThreadHistory = mapping is not null && mapping.ProjectId > 0;
        var isLinked = mapping?.Status == ThreadMappingStatus.Assigned
                       || (mapping is not null && mapping.ProjectId > 0)
                       || inbox.ProjectId > 0;

        var effectiveProjectId = threadProjectId ?? (inbox.ProjectId > 0 ? inbox.ProjectId : (int?)null);
        var displayProject = threadProject ?? inboxProject;
        var display = BuildDisplayName(
            displayProject?.NameAndNumber,
            displayProject?.Number,
            displayProject?.Title);

        var threadDisplay = hasThreadHistory
            ? BuildDisplayName(
                threadProject?.NameAndNumber,
                threadProject?.Number,
                threadProject?.Title)
            : null;

        return new EmailProjectLinkInfo(
            IsLinked: isLinked && effectiveProjectId is > 0,
            ProjectId: isLinked ? effectiveProjectId : null,
            ProjectNumber: displayProject?.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ProjectName: displayProject?.Title,
            DisplayName: isLinked ? display : null,
            InboxMessageId: inbox.Id,
            ThreadUniqueId: inbox.ThreadUniqueId,
            GmailThreadId: inbox.GmailThreadId,
            ThreadProjectId: hasThreadHistory ? threadProjectId : null,
            ThreadProjectName: threadDisplay,
            HasThreadHistory: hasThreadHistory,
            InboxProjectId: inbox.ProjectId > 0 ? inbox.ProjectId : null);
    }

    private static void AddKey(
        Dictionary<string, EmailProjectLinkInfo> result,
        string? key,
        EmailProjectLinkInfo info)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        result[NormalizeMessageId(key)] = info;
        result[key.Trim()] = info;
    }

    private static string NormalizeMessageId(string value) => value.Trim().Trim('<', '>');

    private static string? BuildDisplayName(string? nameAndNumber, float? number, string? title)
    {
        if (!string.IsNullOrWhiteSpace(nameAndNumber))
        {
            return nameAndNumber.Trim();
        }

        if (number is not null && !string.IsNullOrWhiteSpace(title))
        {
            var numberText = number.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return $"{numberText} — {title.Trim()}";
        }

        return title?.Trim();
    }

    private sealed record InboxRow(
        int Id,
        string? InternetMessageId,
        string? MessageUniqueId,
        string? ThreadUniqueId,
        string? GmailThreadId,
        int ProjectId);

    private sealed record MappingRow(
        string ThreadUniqueId,
        string? GmailThreadId,
        int ProjectId,
        ThreadMappingStatus Status);

    private sealed record ProjectRow(
        int Id,
        float? Number,
        string? Title,
        string? NameAndNumber);

    private sealed record ThreadMappingBundle(
        IReadOnlyList<MappingRow> Mappings,
        Dictionary<int, ProjectRow> Projects);
}
