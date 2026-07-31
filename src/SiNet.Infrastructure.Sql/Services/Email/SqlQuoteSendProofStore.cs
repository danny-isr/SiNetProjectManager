using Microsoft.EntityFrameworkCore;
using SiNet.Application.Email.QuoteSend;
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

        db.ProjectAssignmentEvents.Add(new ProjectAssignmentEvent
        {
            ProjectAssignmentId = taskId,
            EventType = EventType,
            Note = $"GmailMessageId={gmailMessageId.Trim()}; Marker={marker}",
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
            .Select(e => new { e.Note, e.EmailThreadId, e.CreatedDate })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return null;

        var messageId = ExtractGmailMessageId(row.Note);
        if (string.IsNullOrWhiteSpace(messageId))
            return null;

        return new QuoteSendProof(taskId, messageId, row.EmailThreadId, row.CreatedDate);
    }

    internal static string? ExtractGmailMessageId(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;

        const string prefix = "GmailMessageId=";
        var start = note.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += prefix.Length;
        var end = note.IndexOf(';', start);
        var value = end < 0 ? note[start..] : note[start..end];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
