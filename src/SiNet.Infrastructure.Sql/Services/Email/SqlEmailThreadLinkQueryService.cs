using Microsoft.EntityFrameworkCore;
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

    public async Task<IReadOnlyDictionary<string, EmailProjectLinkInfo>> GetLinkStatesByInternetMessageIdsAsync(
        IReadOnlyList<string> internetMessageIds,
        CancellationToken cancellationToken = default)
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
            .Select(message => new
            {
                message.Id,
                message.InternetMessageId,
                message.MessageUniqueId,
                message.ThreadUniqueId,
                message.GmailThreadId,
                message.ProjectId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (inboxRows.Count == 0)
        {
            return new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);
        }

        var threadKeys = inboxRows
            .Select(static row => row.ThreadUniqueId)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var mappings = await db.ThreadStatusMappings
            .AsNoTracking()
            .Where(mapping => threadKeys.Contains(mapping.ThreadUniqueId))
            .Select(mapping => new
            {
                mapping.ThreadUniqueId,
                mapping.ProjectId,
                mapping.Status,
                ProjectNumber = mapping.Project.Number,
                ProjectTitle = mapping.Project.Title,
                ProjectNameAndNumber = mapping.Project.NameAndNumber,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mappingByThread = mappings
            .GroupBy(static row => row.ThreadUniqueId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        var projectIds = inboxRows
            .Select(static row => row.ProjectId)
            .Concat(mappings.Select(static row => row.ProjectId))
            .Distinct()
            .ToList();

        var projects = await db.Projects
            .AsNoTracking()
            .Where(project => projectIds.Contains(project.Id))
            .Select(project => new
            {
                project.Id,
                project.Number,
                project.Title,
                project.NameAndNumber,
            })
            .ToDictionaryAsync(static project => project.Id, cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<string, EmailProjectLinkInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var inbox in inboxRows)
        {
            mappingByThread.TryGetValue(inbox.ThreadUniqueId, out var mapping);

            var projectId = mapping?.ProjectId ?? inbox.ProjectId;
            projects.TryGetValue(projectId, out var project);

            var isLinked = mapping?.Status == ThreadMappingStatus.Assigned
                || (mapping is not null && mapping.ProjectId > 0);

            var display = BuildDisplayName(
                project?.NameAndNumber,
                project?.Number,
                project?.Title);

            var info = new EmailProjectLinkInfo(
                IsLinked: isLinked && projectId > 0,
                ProjectId: isLinked ? projectId : null,
                ProjectNumber: project?.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ProjectName: project?.Title,
                DisplayName: isLinked ? display : null,
                InboxMessageId: inbox.Id,
                ThreadUniqueId: inbox.ThreadUniqueId,
                GmailThreadId: inbox.GmailThreadId);

            AddKey(result, inbox.InternetMessageId, info);
            AddKey(result, inbox.MessageUniqueId, info);
        }

        return result;
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
}
