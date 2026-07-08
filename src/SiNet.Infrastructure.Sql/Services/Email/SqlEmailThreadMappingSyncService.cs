using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email;

/// <summary>
/// Backfills <c>ThreadStatusMapping</c> from Gmail project labels on mailbox load (legacy sync parity).
/// </summary>
public sealed class SqlEmailThreadMappingSyncService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IEmailThreadMappingSyncService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task SyncFiledThreadsFromSummariesAsync(
        IReadOnlyList<EmailSummary> summaries,
        CancellationToken cancellationToken = default)
    {
        if (summaries.Count == 0)
        {
            return;
        }

        var candidates = summaries
            .Where(static summary => !string.IsNullOrWhiteSpace(summary.ThreadId))
            .Select(summary =>
            {
                var labelPath = EmailGmailLabelNames.FindProjectLabelPath(summary.LabelNames);
                var parsed = EmailProjectLabelParser.TryParseProjectFromLabelPath(labelPath);
                return new
                {
                    summary.ThreadId,
                    LabelPath = labelPath,
                    Parsed = parsed,
                };
            })
            .Where(static row => row.Parsed is not null && row.Parsed.Value.ProjectId is > 0)
            .GroupBy(static row => row.ThreadId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var threadIds = candidates
            .Select(static row => row.ThreadId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var gmailToUnique = await ResolveThreadUniqueIdsAsync(db, threadIds, cancellationToken).ConfigureAwait(false);
        var uniqueIds = gmailToUnique.Values.Distinct(StringComparer.Ordinal).ToList();

        var existingMappings = await db.ThreadStatusMappings
            .AsNoTracking()
            .Where(mapping =>
                (mapping.ThreadId != null && threadIds.Contains(mapping.ThreadId))
                || (uniqueIds.Count > 0 && uniqueIds.Contains(mapping.ThreadUniqueId)))
            .Select(mapping => new
            {
                mapping.ThreadUniqueId,
                mapping.ThreadId,
                mapping.ProjectId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var synced = 0;
        foreach (var candidate in candidates)
        {
            var labelProjectId = candidate.Parsed!.Value.ProjectId!.Value;
            var labelProjectName = candidate.Parsed.Value.ProjectDisplayName ?? string.Empty;

            var hasThreadHistory = existingMappings.Any(mapping =>
                (string.Equals(mapping.ThreadId, candidate.ThreadId, StringComparison.OrdinalIgnoreCase)
                 && mapping.ProjectId > 0)
                || (gmailToUnique.TryGetValue(candidate.ThreadId, out var uid)
                    && string.Equals(mapping.ThreadUniqueId, uid, StringComparison.Ordinal)
                    && mapping.ProjectId > 0));

            var isConflict = hasThreadHistory
                             && existingMappings.Any(mapping =>
                                 mapping.ProjectId > 0
                                 && mapping.ProjectId != labelProjectId
                                 && ((mapping.ThreadId != null
                                      && string.Equals(mapping.ThreadId, candidate.ThreadId, StringComparison.OrdinalIgnoreCase))
                                     || (gmailToUnique.TryGetValue(candidate.ThreadId, out var uid)
                                         && string.Equals(mapping.ThreadUniqueId, uid, StringComparison.Ordinal))));

            if (hasThreadHistory && !isConflict)
            {
                continue;
            }

            if (await SaveThreadProjectAsync(
                    db,
                    candidate.ThreadId,
                    labelProjectId,
                    labelProjectName,
                    gmailToUnique,
                    cancellationToken).ConfigureAwait(false))
            {
                synced++;
            }
        }

        if (synced > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[ThreadMappingSync] Persisted {synced} thread assignments from Gmail labels.");
        }
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

    private static async Task<bool> SaveThreadProjectAsync(
        SiNetSQLDbContext db,
        string gmailThreadId,
        int projectId,
        string? projectName,
        IReadOnlyDictionary<string, string> gmailToUnique,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gmailThreadId) || projectId <= 0)
        {
            return false;
        }

        var threadUniqueId = gmailToUnique.TryGetValue(gmailThreadId, out var uid) ? uid : null;
        if (string.IsNullOrWhiteSpace(threadUniqueId))
        {
            return false;
        }

        var existing = await db.ThreadStatusMappings
            .FirstOrDefaultAsync(
                mapping => mapping.ThreadUniqueId == threadUniqueId,
                cancellationToken)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;
        if (existing is not null)
        {
            existing.ProjectId = projectId;
            existing.Status = ThreadMappingStatus.Assigned;
            existing.LastUpdated = now;
            existing.ThreadId = gmailThreadId;
        }
        else
        {
            db.ThreadStatusMappings.Add(new ThreadStatusMapping
            {
                ThreadUniqueId = threadUniqueId,
                ThreadId = gmailThreadId,
                ProjectId = projectId,
                Status = ThreadMappingStatus.Assigned,
                LastUpdated = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine(
            $"[ThreadMappingSync] Linked thread {gmailThreadId} to project {projectId} ({projectName ?? "unnamed"})");
        return true;
    }
}
