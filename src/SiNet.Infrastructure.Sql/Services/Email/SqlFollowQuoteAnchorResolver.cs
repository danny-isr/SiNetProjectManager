using Microsoft.EntityFrameworkCore;
using SiNet.Application.Diagnostics;
using SiNet.Application.Email.QuoteSend;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email;

/// <summary>
/// Resolves the latest SendQuote proof on the FollowQuote task's project for Email-first open.
/// </summary>
public sealed class SqlFollowQuoteAnchorResolver(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IQuoteSendProofStore proofStore) : IFollowQuoteAnchorResolver
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IQuoteSendProofStore _proofStore =
        proofStore ?? throw new ArgumentNullException(nameof(proofStore));

    public async Task<FollowQuoteOpenAnchor?> ResolveAsync(
        int followQuoteTaskId,
        CancellationToken cancellationToken = default)
    {
        if (followQuoteTaskId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var task = await db.ProjectAssignments
            .AsNoTracking()
            .Where(t => t.Id == followQuoteTaskId)
            .Select(t => new { t.Id, t.ProjectId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (task?.ProjectId is not int projectId || projectId <= 0)
        {
            WorkflowDebugTrace.Step(
                "FollowQuote.Anchor",
                $"task={followQuoteTaskId} resolve=null reason=no-project");
            return null;
        }

        var proof = await _proofStore
            .GetLatestForProjectAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        // Prefer active workflow instance id (same rule as task navigation).
        int? workflowInstanceId = await db.WorkflowInstances
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId
                        && i.Status != WorkflowStatus.Completed
                        && i.Status != WorkflowStatus.Cancelled)
            .OrderByDescending(i => i.Id)
            .Select(i => (int?)i.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var anchor = new FollowQuoteOpenAnchor(
            FollowQuoteTaskId: followQuoteTaskId,
            ProjectId: projectId,
            WorkflowInstanceId: workflowInstanceId,
            GmailThreadId: proof?.GmailThreadId,
            SentGmailMessageId: proof?.GmailMessageId,
            CounterpartAddress: proof?.PrimaryTo);

        WorkflowDebugTrace.Step(
            "FollowQuote.Anchor",
            $"task={followQuoteTaskId} project={projectId} thread={(anchor.GmailThreadId ?? "-")} to={(anchor.CounterpartAddress ?? "-")} sentMsg={(anchor.SentGmailMessageId ?? "-")}");

        return anchor;
    }
}
