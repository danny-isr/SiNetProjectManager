using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Email;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email;

public sealed class SqlEmailFilingService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IEmailGmailModifyService gmailModify) : IEmailFilingService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly IEmailGmailModifyService _gmailModify =
        gmailModify ?? throw new ArgumentNullException(nameof(gmailModify));

    public async Task<EmailFilingResult> FileToProjectAsync(
        FileEmailToProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.InboxMessageId <= 0)
        {
            return new EmailFilingResult(false, "Missing inbox message id.");
        }

        if (command.TargetProjectId <= 0)
        {
            return new EmailFilingResult(false, "Invalid target project.");
        }

        if (string.IsNullOrWhiteSpace(command.GmailMessageId))
        {
            return new EmailFilingResult(false, "Missing Gmail message id.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var inbox = await db.EmailInboxMessages
            .AsNoTracking()
            .Where(message => message.Id == command.InboxMessageId)
            .Select(message => new
            {
                message.Id,
                message.ThreadUniqueId,
                message.GmailThreadId,
                message.InternetMessageId,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (inbox is null)
        {
            return new EmailFilingResult(false, "Inbox message not found.");
        }

        var project = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == command.TargetProjectId)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.NameAndNumber,
                PlaceTitle = p.Place != null ? p.Place.Title : null,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (project is null)
        {
            return new EmailFilingResult(false, $"Project {command.TargetProjectId} not found.");
        }

        var location = EmailProjectLabelFormatter.GetLocation(project.PlaceTitle);
        var projectDisplayName = EmailProjectLabelFormatter.FormatProjectName(
            project.Id,
            project.NameAndNumber,
            project.Title);

        try
        {
            var labelId = await _gmailModify
                .GetOrCreateProjectLabelAsync(location, projectDisplayName, cancellationToken)
                .ConfigureAwait(false);
            await _gmailModify
                .AttachProjectLabelAsync(command.GmailMessageId, labelId, cancellationToken)
                .ConfigureAwait(false);

            var gmailThreadId = command.GmailThreadId ?? inbox.GmailThreadId;
            if (!string.IsNullOrWhiteSpace(gmailThreadId) && !string.IsNullOrWhiteSpace(inbox.ThreadUniqueId))
            {
                var mapping = await db.ThreadStatusMappings
                    .FirstOrDefaultAsync(
                        m => m.ThreadUniqueId == inbox.ThreadUniqueId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (mapping is null)
                {
                    db.ThreadStatusMappings.Add(new ThreadStatusMapping
                    {
                        ThreadUniqueId = inbox.ThreadUniqueId,
                        ThreadId = gmailThreadId,
                        ProjectId = command.TargetProjectId,
                        Status = ThreadMappingStatus.Assigned,
                        LastUpdated = DateTime.UtcNow,
                    });
                }
                else
                {
                    mapping.ProjectId = command.TargetProjectId;
                    mapping.Status = ThreadMappingStatus.Assigned;
                    mapping.ThreadId = gmailThreadId;
                    mapping.LastUpdated = DateTime.UtcNow;
                }
            }

            await db.EmailInboxMessages
                .Where(message => message.Id == command.InboxMessageId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(message => message.ProjectId, command.TargetProjectId)
                        .SetProperty(
                            message => message.GmailThreadId,
                            message => message.GmailThreadId ?? gmailThreadId),
                    cancellationToken)
                .ConfigureAwait(false);

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new EmailFilingResult(true, AssignedProjectId: command.TargetProjectId);
        }
        catch (Exception ex)
        {
            return new EmailFilingResult(false, ex.Message);
        }
    }

    public async Task<EmailFilingResult> UnfileFromProjectAsync(
        UnfileEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.InboxMessageId <= 0)
        {
            return new EmailFilingResult(false, "Missing inbox message id.");
        }

        if (string.IsNullOrWhiteSpace(command.GmailMessageId))
        {
            return new EmailFilingResult(false, "Missing Gmail message id.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var inbox = await db.EmailInboxMessages
            .AsNoTracking()
            .Where(message => message.Id == command.InboxMessageId)
            .Select(message => new
            {
                message.Id,
                message.ProjectId,
                message.ThreadUniqueId,
                message.GmailThreadId,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (inbox is null)
        {
            return new EmailFilingResult(false, "Inbox message not found.");
        }

        var project = await db.Projects
            .AsNoTracking()
            .Where(p => p.Id == inbox.ProjectId)
            .Select(p => new
            {
                p.NameAndNumber,
                p.Title,
                PlaceTitle = p.Place != null ? p.Place.Title : null,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (project is null)
        {
            return new EmailFilingResult(false, "Project metadata not found.");
        }

        var location = EmailProjectLabelFormatter.GetLocation(project.PlaceTitle);
        var projectDisplayName = EmailProjectLabelFormatter.FormatProjectName(
            inbox.ProjectId,
            project.NameAndNumber,
            project.Title);

        try
        {
            var labelId = await _gmailModify
                .GetProjectLabelIdAsync(location, projectDisplayName, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(labelId))
            {
                await _gmailModify
                    .RemoveProjectLabelAsync(command.GmailMessageId, labelId, moveToInbox: true, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(inbox.ThreadUniqueId))
            {
                await db.ThreadStatusMappings
                    .Where(mapping => mapping.ThreadUniqueId == inbox.ThreadUniqueId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            var officeProjectId = await db.Projects
                .AsNoTracking()
                .Where(p => p.Title == "ניהול  משרד - כללי")
                .Select(p => p.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (officeProjectId > 0)
            {
                await db.EmailInboxMessages
                    .Where(message => message.Id == command.InboxMessageId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(message => message.ProjectId, officeProjectId),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return new EmailFilingResult(true);
        }
        catch (Exception ex)
        {
            return new EmailFilingResult(false, ex.Message);
        }
    }
}
