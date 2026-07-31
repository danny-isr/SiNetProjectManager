using Microsoft.EntityFrameworkCore;
using SiNet.Application.Email.QuoteSend;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email;

/// <summary>Resolves the inbox message that triggered a Proposal workflow instance.</summary>
public sealed class SqlProposalSourceEmailQuery(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IProposalSourceEmailQuery
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<ProposalSourceEmailRef?> GetByWorkflowInstanceAsync(
        int workflowInstanceId,
        CancellationToken cancellationToken = default)
    {
        if (workflowInstanceId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .Where(w => w.Id == workflowInstanceId)
            .Select(w => new { w.TriggerType, w.TriggerEntityId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (instance is null
            || instance.TriggerType != WorkflowTriggerType.Email
            || instance.TriggerEntityId is not int inboxId
            || inboxId <= 0)
        {
            return null;
        }

        var row = await db.EmailInboxMessages
            .AsNoTracking()
            .Where(m => m.Id == inboxId)
            .Select(m => new ProposalSourceEmailRef(
                m.Id,
                m.Subject,
                m.FromAddress,
                m.InternetMessageId,
                m.GmailThreadId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row;
    }
}
