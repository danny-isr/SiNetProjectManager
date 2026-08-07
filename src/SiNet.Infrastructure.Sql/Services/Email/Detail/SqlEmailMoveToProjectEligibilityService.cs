using Microsoft.EntityFrameworkCore;
using SiNet.Application.Email.Detail;
using SiNet.Infrastructure.Sql.Services.Email.Acc;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Detail;

internal sealed class SqlEmailMoveToProjectEligibilityService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IEmailMoveToProjectEligibilityService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<EmailMoveToProjectEligibility> EvaluateAsync(
        EmailMoveToProjectEligibilityQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.InboxMessageId <= 0)
        {
            return Block("בחר מייל.");
        }

        if (query.ProjectId <= 0)
        {
            return Block("בחר פרויקט.");
        }

        if (!query.IsEmailFiledToProject)
        {
            return Block("המייל לא משויך לפרויקט.");
        }

        if (query.AttachmentCount <= 0)
        {
            return Allow();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var attachments = await db.EmailInboxAttachments
            .AsNoTracking()
            .Where(a => a.MessageId == query.InboxMessageId)
            .Select(a => new
            {
                a.AttachmentIndex,
                a.ProjectFileId,
                a.ProjectAlternativeId,
                a.AccItemId,
                a.SavedFileName,
                a.OriginalFileName,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var taggable = attachments
            .Where(a => IsTaggableAttachment(a.AttachmentIndex, a.SavedFileName ?? a.OriginalFileName))
            .ToList();

        if (taggable.Count == 0)
        {
            if (attachments.Count == 0)
            {
                return Block("ממתין לסנכרון צרופות מ-ACC. נסה שוב בעוד רגע.");
            }

            return Allow();
        }

        var untagged = taggable.Count(a => a.ProjectFileId is null or <= 0);
        if (untagged > 0)
        {
            return Block(EmailMoveToProjectEligibilityRules.UntaggedAttachmentsMessage(untagged));
        }

        if (EmailMoveToProjectEligibilityRules.HasDuplicateFilingTargets(
                taggable.Select(a => (a.ProjectFileId!.Value, a.ProjectAlternativeId))))
        {
            return Block(EmailMoveToProjectEligibilityRules.DuplicateTargetMessage);
        }

        var unplaced = taggable.Count(a => string.IsNullOrWhiteSpace(a.AccItemId));
        if (unplaced == taggable.Count)
        {
            return Block("שירות ה-ACC (Ingestion) אינו זמין.");
        }

        return Allow();
    }

    private static bool IsTaggableAttachment(int attachmentIndex, string? fileName) =>
        !string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase)
        && (
            attachmentIndex >= 0
            || (attachmentIndex == AccInboxLayout.EmailBodyAttachmentIndex
                && string.Equals(fileName, AccInboxLayout.EmailBodyFileName, StringComparison.OrdinalIgnoreCase)));

    private static EmailMoveToProjectEligibility Allow() => new(true, null);

    private static EmailMoveToProjectEligibility Block(string reason) => new(false, reason);
}
