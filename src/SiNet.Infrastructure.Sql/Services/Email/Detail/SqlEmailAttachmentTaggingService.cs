using Microsoft.EntityFrameworkCore;
using SiNet.Application.Email.Detail;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Detail;

internal sealed class SqlEmailAttachmentTaggingService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IEmailAttachmentTaggingService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<IReadOnlyList<EmailInboxAttachmentTagState>> LoadInboxAttachmentsAsync(
        int inboxMessageId,
        CancellationToken cancellationToken = default)
    {
        if (inboxMessageId <= 0)
        {
            return Array.Empty<EmailInboxAttachmentTagState>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var attachments = await db.EmailInboxAttachments
            .AsNoTracking()
            .Where(a => a.MessageId == inboxMessageId)
            .OrderBy(a => a.AttachmentIndex)
            .Select(a => new
            {
                a.Id,
                a.AttachmentIndex,
                FileName = a.SavedFileName ?? a.OriginalFileName ?? "(ללא שם)",
                a.ProjectFileId,
                ProjectFileTitle = a.ProjectFile != null ? a.ProjectFile.Title : null,
                a.ProjectAlternativeId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return attachments
            .Select(a => new EmailInboxAttachmentTagState(
                a.Id,
                a.FileName,
                a.AttachmentIndex,
                a.ProjectFileId,
                a.ProjectFileTitle,
                a.ProjectAlternativeId,
                IsTaggableAttachment(a.AttachmentIndex, a.FileName)))
            .ToList();
    }

    public async Task<IReadOnlyList<EmailProjectAlternativeOption>> LoadAlternativesAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return Array.Empty<EmailProjectAlternativeOption>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var alternatives = await db.ProjectAlternatives
            .AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (alternatives.Count == 0)
        {
            return
            [
                new EmailProjectAlternativeOption(1, "1", IsDefault: true),
            ];
        }

        return alternatives
            .Select((a, index) => new EmailProjectAlternativeOption(
                a.Id,
                string.IsNullOrWhiteSpace(a.Name) ? a.Id.ToString() : a.Name!,
                index == 0))
            .ToList();
    }

    public async Task<IReadOnlyList<EmailAttachmentTagTarget>> LoadTagTargetsAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return Array.Empty<EmailAttachmentTagTarget>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var jobTypeIds = await db.Set<TypeOfProjectInProject>()
            .AsNoTracking()
            .Where(tp => tp.ProjectId == projectId && tp.ProjectTypeId != null)
            .Select(tp => tp.ProjectTypeId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IQueryable<ProjectFile> query = db.ProjectFiles.AsNoTracking();

        if (jobTypeIds.Count > 0)
        {
            query = query.Where(pf =>
                pf.OutSidData == true
                && pf.TypeProjId != null
                && jobTypeIds.Contains(pf.TypeProjId.Value));

            var strictCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
            if (strictCount == 0)
            {
                query = db.ProjectFiles.AsNoTracking()
                    .Where(pf => pf.TypeProjId != null && jobTypeIds.Contains(pf.TypeProjId.Value));
            }
        }
        else
        {
            query = query.Where(pf => pf.Folderid != null);
        }

        var files = await query
            .OrderBy(pf => pf.Title)
            .ThenBy(pf => pf.Number)
            .Select(pf => new { pf.Id, pf.Title })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var alternativeCount = await db.ProjectAlternatives
            .AsNoTracking()
            .CountAsync(a => a.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);

        return files
            .Select(f => new EmailAttachmentTagTarget(
                f.Id,
                f.Title ?? $"ProjectFile #{f.Id}",
                alternativeCount > 1))
            .ToList();
    }

    public async Task<EmailAttachmentTagValidationResult> ValidateTagAsync(
        EmailAttachmentTagValidationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.InboxAttachmentId <= 0 || query.ProjectFileId <= 0)
        {
            return new EmailAttachmentTagValidationResult(false, "פרמטרים לא תקינים לתיוג.", false);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var duplicateOnMessage = await db.EmailInboxAttachments
            .AsNoTracking()
            .AnyAsync(
                a => a.MessageId == query.InboxMessageId
                     && a.Id != query.InboxAttachmentId
                     && a.ProjectFileId == query.ProjectFileId
                     && a.ProjectAlternativeId == query.ProjectAlternativeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (duplicateOnMessage)
        {
            return new EmailAttachmentTagValidationResult(
                false,
                "קובץ אחר במייל כבר משויך לאותו יעד. בחר יעד שונה.",
                false);
        }

        var alreadyTaggedSameTarget = await db.EmailInboxAttachments
            .AsNoTracking()
            .AnyAsync(
                a => a.Id == query.InboxAttachmentId
                     && a.ProjectFileId == query.ProjectFileId
                     && a.ProjectAlternativeId == query.ProjectAlternativeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (alreadyTaggedSameTarget)
        {
            return new EmailAttachmentTagValidationResult(true, null, false);
        }

        var hasExistingAccPlacement = await db.EmailInboxAttachments
            .AsNoTracking()
            .AnyAsync(
                a => a.ProjectFileId == query.ProjectFileId
                     && a.MessageId != query.InboxMessageId
                     && !string.IsNullOrWhiteSpace(a.AccItemId),
                cancellationToken)
            .ConfigureAwait(false);

        return new EmailAttachmentTagValidationResult(true, null, hasExistingAccPlacement);
    }

    public async Task<EmailAttachmentTagResult> SetTagAsync(
        EmailAttachmentTagCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.InboxAttachmentId <= 0 || command.ProjectFileId <= 0)
        {
            return new EmailAttachmentTagResult(false, "פרמטרים לא תקינים לתיוג.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var attachment = await db.EmailInboxAttachments
            .FirstOrDefaultAsync(a => a.Id == command.InboxAttachmentId, cancellationToken)
            .ConfigureAwait(false);

        if (attachment is null)
        {
            return new EmailAttachmentTagResult(false, "הצרופה לא נמצאה.");
        }

        var projectFile = await db.ProjectFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(pf => pf.Id == command.ProjectFileId, cancellationToken)
            .ConfigureAwait(false);

        if (projectFile is null || projectFile.OutSidData != true)
        {
            return new EmailAttachmentTagResult(false, "סוג קובץ פרויקט לא תקין לתיוג.");
        }

        attachment.ProjectFileId = command.ProjectFileId;
        attachment.ProjectAlternativeId = command.ProjectAlternativeId;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new EmailAttachmentTagResult(true, null);
    }

    private static bool IsTaggableAttachment(int attachmentIndex, string fileName) =>
        attachmentIndex >= 0
        && !string.Equals(fileName, "00_Email.pdf", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase);
}
