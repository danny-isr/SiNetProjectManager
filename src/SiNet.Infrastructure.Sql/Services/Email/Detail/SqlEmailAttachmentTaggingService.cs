using Microsoft.EntityFrameworkCore;
using SiNet.Application.Diagnostics;
using SiNet.Application.Email.Detail;
using SiNet.Infrastructure.Sql.Services.Email.Acc;
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
                a.AccItemId,
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
                IsTaggableAttachment(a.AttachmentIndex, a.FileName),
                a.AccItemId))
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

        // Match legacy AttachmentTaggingService.EnsureAndLoadAlternativesAsync:
        // never invent a fake Id — auto-create a real default "1" row when none exist.
        var alternatives = await db.ProjectAlternatives
            .Where(a => a.ProjectId == projectId && a.IsActive)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Corrupt/legacy rows with Id=0 break tagging: ResolveDefaultId and SetTag require Id > 0.
        // Observed after DB restore when ProjectAlternative had a single "1" row with ID=0.
        var invalidIdRows = alternatives.Where(static a => a.Id <= 0).ToList();
        if (invalidIdRows.Count > 0)
        {
            // #region agent log
            WorkflowDebugTrace.Step(
                "Email.TagUI",
                $"H-ALT2 repair-invalid-alt-ids project={projectId} count={invalidIdRows.Count} ids=[{string.Join(",", invalidIdRows.Select(a => a.Id))}]");
            // #endregion
            db.ProjectAlternatives.RemoveRange(invalidIdRows);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            alternatives = await db.ProjectAlternatives
                .Where(a => a.ProjectId == projectId && a.IsActive)
                .OrderBy(a => a.SortOrder)
                .ThenBy(a => a.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (alternatives.Count == 0)
        {
            var defaultAlt = new ProjectAlternative
            {
                ProjectId = projectId,
                Name = "1",
                NormalizedName = "1",
                IsPrimary = true,
                SortOrder = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
            db.ProjectAlternatives.Add(defaultAlt);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            alternatives = [defaultAlt];
            // #region agent log
            WorkflowDebugTrace.Step(
                "Email.TagUI",
                $"H-ALT2 created-default-alt project={projectId} id={defaultAlt.Id}");
            // #endregion
        }

        return alternatives
            .Select((a, index) => new EmailProjectAlternativeOption(
                a.Id,
                string.IsNullOrWhiteSpace(a.Name) ? a.Id.ToString() : a.Name!,
                index == 0))
            .ToList();
    }

    public async Task<EmailProjectAlternativeOption?> CreateAlternativeAsync(
        int projectId,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0 || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (!TryNormalizeAlternativeName(name, out var canonical, out var normalizedKey))
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await db.ProjectAlternatives
            .Where(a => a.ProjectId == projectId
                        && a.IsActive
                        && a.NormalizedName == normalizedKey)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return new EmailProjectAlternativeOption(existing.Id, existing.Name, IsDefault: false);
        }

        var nextSort = await db.ProjectAlternatives
            .Where(a => a.ProjectId == projectId)
            .MaxAsync(a => (int?)a.SortOrder, cancellationToken)
            .ConfigureAwait(false) ?? 0;

        var alt = new ProjectAlternative
        {
            ProjectId = projectId,
            Name = canonical,
            NormalizedName = normalizedKey,
            SortOrder = nextSort + 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.ProjectAlternatives.Add(alt);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new EmailProjectAlternativeOption(alt.Id, alt.Name, IsDefault: false);
    }

    /// <summary>
    /// Minimal alternative-name normalization (mirrors SiNetSQL ProjectAlternativeNameRules basics).
    /// </summary>
    private static bool TryNormalizeAlternativeName(string raw, out string canonical, out string normalizedKey)
    {
        canonical = string.Empty;
        normalizedKey = string.Empty;

        var input = raw.Trim();
        if (input.Length is 0 or > 20)
        {
            return false;
        }

        if (input.Contains('-')
            || input.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            return false;
        }

        var tildeCount = input.Count(c => c == '~');
        if (tildeCount > 1 || input.StartsWith('~') || input.EndsWith('~'))
        {
            return false;
        }

        if (tildeCount == 1)
        {
            var parts = input.Split('~');
            input = $"{parts[0].Trim()}~{parts[1].Trim()}";
        }

        canonical = input;
        normalizedKey = input.ToLowerInvariant();
        return true;
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

        // All OutSidData catalog slots (all project types) — same scope as the picker.
        var files = await db.ProjectFiles
            .AsNoTracking()
            .Where(pf => pf.OutSidData == true)
            .OrderBy(pf => pf.Title)
            .ThenBy(pf => pf.Number)
            .Select(pf => new { pf.Id, pf.Title, TypeTitle = pf.TypeProj != null ? pf.TypeProj.Title : null })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var alternativeCount = await db.ProjectAlternatives
            .AsNoTracking()
            .CountAsync(a => a.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);

        return files
            .Select(f =>
            {
                var title = string.IsNullOrWhiteSpace(f.Title) ? $"#{f.Id}" : f.Title!;
                var display = string.IsNullOrWhiteSpace(f.TypeTitle)
                    ? title
                    : $"[{f.TypeTitle}] {title}";
                return new EmailAttachmentTagTarget(f.Id, display, alternativeCount > 1);
            })
            .ToList();
    }

    public async Task<EmailAttachmentTagPickerCatalog> LoadTagPickerCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var fileRows = await db.ProjectFiles
            .AsNoTracking()
            .Where(pf => pf.OutSidData == true)
            .OrderBy(pf => pf.Number)
            .ThenBy(pf => pf.Title)
            .Select(pf => new
            {
                pf.Id,
                pf.Title,
                pf.TypeProjId,
                TypeTitle = pf.TypeProj != null ? pf.TypeProj.Title : null,
                FolderId = pf.Folder != null ? (int?)pf.Folder.Id : pf.Folderid,
                pf.Number,
                pf.IsRequired,
                pf.Code,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var files = fileRows
            .Select(f => new EmailAttachmentTagPickerFile(
                f.Id,
                string.IsNullOrWhiteSpace(f.Title) ? "(ללא שם)" : f.Title!,
                f.TypeProjId,
                f.TypeTitle,
                f.FolderId,
                f.Number,
                f.IsRequired,
                f.Code))
            .ToList();

        var seedFolderIds = files
            .Where(f => f.FolderId is > 0)
            .Select(f => f.FolderId!.Value)
            .Distinct()
            .ToList();

        var folderMap = new Dictionary<int, EmailAttachmentTagPickerFolder>();
        var pending = seedFolderIds.ToList();
        while (pending.Count > 0)
        {
            var batch = await db.ProjectFolders
                .AsNoTracking()
                .Where(f => pending.Contains(f.Id))
                .Select(f => new { f.Id, f.Title, f.Infolderid })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            pending.Clear();
            foreach (var f in batch)
            {
                if (folderMap.ContainsKey(f.Id))
                {
                    continue;
                }

                folderMap[f.Id] = new EmailAttachmentTagPickerFolder(
                    f.Id,
                    string.IsNullOrWhiteSpace(f.Title) ? "(תיקייה ללא שם)" : f.Title!,
                    f.Infolderid);

                if (f.Infolderid is int parentId && parentId > 0 && !folderMap.ContainsKey(parentId))
                {
                    pending.Add(parentId);
                }
            }
        }

        var jobTypes = await db.JobTypes
            .AsNoTracking()
            .Where(jt => jt.ProjectFiles.Any(pf => pf.OutSidData == true))
            .OrderBy(jt => jt.Title)
            .Select(jt => new { jt.Id, jt.Title })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var jobTypeDtos = jobTypes
            .Select(jt => new EmailAttachmentTagPickerJobType(
                jt.Id,
                string.IsNullOrWhiteSpace(jt.Title) ? $"סוג #{jt.Id}" : jt.Title!))
            .ToList();

        return new EmailAttachmentTagPickerCatalog(files, folderMap.Values.ToList(), jobTypeDtos);
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

        int? resolvedAltId = command.ProjectAlternativeId;
        if (resolvedAltId is int aid && aid > 0)
        {
            var altExists = await db.ProjectAlternatives.AsNoTracking()
                .AnyAsync(a => a.Id == aid, cancellationToken)
                .ConfigureAwait(false);
            if (!altExists)
            {
                return new EmailAttachmentTagResult(false, "אלטרנטיבה לא תקינה לפרויקט. רענן את המייל ונסה שוב.");
            }
        }
        else
        {
            resolvedAltId = null;
        }

        attachment.ProjectFileId = command.ProjectFileId;
        attachment.ProjectAlternativeId = resolvedAltId;

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new EmailAttachmentTagResult(true, null);
        }
        catch (Exception)
        {
            return new EmailAttachmentTagResult(false, "שמירת התיוג נכשלה. נסה שוב.");
        }
    }

    private static bool IsTaggableAttachment(int attachmentIndex, string fileName) =>
        !string.Equals(fileName, "manifest.json", StringComparison.OrdinalIgnoreCase)
        && (
            attachmentIndex >= 0
            || (attachmentIndex == AccInboxLayout.EmailBodyAttachmentIndex
                && string.Equals(fileName, AccInboxLayout.EmailBodyFileName, StringComparison.OrdinalIgnoreCase)));
}
