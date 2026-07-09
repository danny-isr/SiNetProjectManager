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
}
