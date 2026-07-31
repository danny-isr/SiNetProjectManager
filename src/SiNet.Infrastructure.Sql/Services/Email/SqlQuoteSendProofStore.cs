using Microsoft.EntityFrameworkCore;
using SiNet.Application.Email.QuoteSend;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email;

/// <summary>
/// Persists SendQuote proof as a <see cref="ProjectAssignmentEvent"/> so completion
/// does not rely on Sent-folder marker search.
/// </summary>
public sealed class SqlQuoteSendProofStore(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IQuoteSendProofStore
{
    public const string EventType = "QuoteSendProof";

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task SaveAsync(
        int taskId,
        int actingUserId,
        string gmailMessageId,
        string? gmailThreadId,
        string marker,
        string? primaryTo = null,
        CancellationToken cancellationToken = default)
    {
        if (taskId <= 0)
            throw new ArgumentOutOfRangeException(nameof(taskId));
        if (actingUserId <= 0)
            throw new ArgumentOutOfRangeException(nameof(actingUserId));
        ArgumentException.ThrowIfNullOrWhiteSpace(gmailMessageId);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var exists = await db.ProjectAssignments
            .AsNoTracking()
            .AnyAsync(t => t.Id == taskId, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
            throw new InvalidOperationException($"Task {taskId} was not found.");

        var note = $"GmailMessageId={gmailMessageId.Trim()}; Marker={marker}";
        if (!string.IsNullOrWhiteSpace(primaryTo))
            note += $"; PrimaryTo={primaryTo.Trim()}";

        db.ProjectAssignmentEvents.Add(new ProjectAssignmentEvent
        {
            ProjectAssignmentId = taskId,
            EventType = EventType,
            Note = note,
            EmailThreadId = string.IsNullOrWhiteSpace(gmailThreadId) ? null : gmailThreadId.Trim(),
            CreatedByUserId = actingUserId,
            CreatedDate = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<QuoteSendProof?> GetLatestAsync(int taskId, CancellationToken cancellationToken = default)
    {
        if (taskId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var row = await db.ProjectAssignmentEvents
            .AsNoTracking()
            .Where(e => e.ProjectAssignmentId == taskId && e.EventType == EventType)
            .OrderByDescending(e => e.CreatedDate)
            .Select(e => new { e.ProjectAssignmentId, e.Note, e.EmailThreadId, e.CreatedDate })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToProof(row.ProjectAssignmentId, row.Note, row.EmailThreadId, row.CreatedDate);
    }

    public async Task<QuoteSendProof?> GetLatestForProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var sendQuoteTaskIds = await (
                from t in db.ProjectAssignments.AsNoTracking()
                join tt in db.TaskTypes.AsNoTracking() on t.TaskTypeId equals tt.Id
                where t.ProjectId == projectId && tt.Code == TaskTypeCodes.SendQuoteToClient
                select t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sendQuoteTaskIds.Count == 0)
            return null;

        var row = await db.ProjectAssignmentEvents
            .AsNoTracking()
            .Where(e => sendQuoteTaskIds.Contains(e.ProjectAssignmentId) && e.EventType == EventType)
            .OrderByDescending(e => e.CreatedDate)
            .Select(e => new { e.ProjectAssignmentId, e.Note, e.EmailThreadId, e.CreatedDate })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? null
            : ToProof(row.ProjectAssignmentId, row.Note, row.EmailThreadId, row.CreatedDate);
    }

    private static QuoteSendProof? ToProof(int taskId, string? note, string? threadId, DateTime created)
    {
        var messageId = ExtractGmailMessageId(note);
        if (string.IsNullOrWhiteSpace(messageId))
            return null;

        return new QuoteSendProof(
            taskId,
            messageId,
            threadId,
            created,
            ExtractField(note, "PrimaryTo="));
    }

    internal static string? ExtractGmailMessageId(string? note)
        => ExtractField(note, "GmailMessageId=");

    internal static string? ExtractField(string? note, string prefix)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;

        var start = note.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += prefix.Length;
        var end = note.IndexOf(';', start);
        var value = end < 0 ? note[start..] : note[start..end];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
