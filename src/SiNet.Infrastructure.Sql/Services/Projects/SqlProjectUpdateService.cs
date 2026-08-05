using Microsoft.EntityFrameworkCore;
using SiNet.Application.Projects;
using SiNet.Application.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Projects;

internal sealed class SqlProjectUpdateService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory) : IProjectUpdateService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<ProjectEditDto?> GetForEditAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var project = await db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
            return null;

        var linked = await db.TypeOfProjectInProjects
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var bids = await db.Bids
            .AsNoTracking()
            .Where(b => b.ProjectsId == projectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var jobTypes = await db.JobTypes
            .AsNoTracking()
            .OrderBy(j => j.Title)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lines = jobTypes.Select(jt =>
        {
            var link = linked.FirstOrDefault(t => t.ProjectTypeId == jt.Id);
            var bid = bids.FirstOrDefault(b => b.JobTypeId == jt.Id);
            return new ProjectJobTypeEditLine(
                jt.Id,
                jt.Title ?? $"#{jt.Id}",
                IsSelected: link is not null,
                AdminWorkerId: link?.AdminWorkerId,
                BidValue: bid?.BidValue ?? 0m);
        }).ToList();

        var numberDisplay = project.Number?.ToString("0")
            ?? project.Id.ToString();

        return new ProjectEditDto(
            project.Id,
            numberDisplay,
            project.Title ?? string.Empty,
            project.NameAndNumber,
            project.PlaceId,
            project.CompanyId,
            project.ContactsId,
            project.OnerProjectId,
            project.ProjectStatusId,
            project.ApproveDescription,
            lines);
    }

    public async Task<IReadOnlyList<ProjectJobTypeRemovalRiskDto>> GetJobTypeRemovalRiskAsync(
        int projectId,
        IReadOnlyCollection<int> remainingJobTypeIds,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
            return [];

        ArgumentNullException.ThrowIfNull(remainingJobTypeIds);
        var remaining = remainingJobTypeIds.Where(id => id > 0).ToHashSet();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var linkedTypeIds = await db.TypeOfProjectInProjects
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.ProjectTypeId != null)
            .Select(t => t.ProjectTypeId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var removedIds = linkedTypeIds.Where(id => !remaining.Contains(id)).Distinct().ToList();
        if (removedIds.Count == 0)
            return [];

        var openStatuses = new[]
        {
            WorkflowStatus.Draft,
            WorkflowStatus.Active,
            WorkflowStatus.Paused,
        };

        var rows = await db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.WorkflowDefinition)
            .Include(i => i.JobType)
            .Where(i => i.ProjectId == projectId
                && i.JobTypeId != null
                && removedIds.Contains(i.JobTypeId.Value)
                && openStatuses.Contains(i.Status))
            .OrderBy(i => i.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(i => new ProjectJobTypeRemovalRiskDto(
                i.Id,
                i.JobTypeId!.Value,
                i.JobType?.Title,
                i.WorkflowDefinition?.Name ?? $"#{i.WorkflowDefinitionId}",
                StatusLabel(i.Status)))
            .ToList();
    }

    public async Task<UpdateProjectResult> SaveAsync(
        UpdateProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ProjectId <= 0)
            return UpdateProjectResult.Fail("מזהה פרויקט לא תקין.");
        if (command.PlaceId <= 0 || command.CompanyId <= 0 || command.ContactId <= 0)
            return UpdateProjectResult.Fail("יש לבחור מקום, חברה ואיש קשר.");

        var selected = (command.JobTypes ?? Array.Empty<ProjectJobTypeEditLine>())
            .Where(l => l.IsSelected && l.JobTypeId > 0)
            .GroupBy(l => l.JobTypeId)
            .Select(g => g.First())
            .ToList();
        if (selected.Count == 0)
            return UpdateProjectResult.Fail("יש לבחור לפחות סוג פרויקט אחד.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == command.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
            return UpdateProjectResult.Fail("הפרויקט לא נמצא.");

        var place = await db.Places.FindAsync([command.PlaceId], cancellationToken).ConfigureAwait(false);
        var company = await db.Companies.FindAsync([command.CompanyId], cancellationToken).ConfigureAwait(false);
        var contact = await db.Contacts.FindAsync([command.ContactId], cancellationToken).ConfigureAwait(false);
        if (place is null || company is null || contact is null)
            return UpdateProjectResult.Fail("מקום, חברה או איש קשר לא נמצאו.");

        if (contact.CompanyId is int contactCompanyId && contactCompanyId != command.CompanyId)
            return UpdateProjectResult.Fail("איש הקשר אינו שייך לחברה שנבחרה.");

        if (command.ParentProjectId is int parentId and > 0)
        {
            if (parentId == command.ProjectId)
                return UpdateProjectResult.Fail("פרויקט לא יכול להיות אב של עצמו.");
            var parent = await db.Projects.FindAsync([parentId], cancellationToken).ConfigureAwait(false);
            if (parent is null)
                return UpdateProjectResult.Fail("פרויקט האב לא נמצא.");
            project.OnerProjectId = parentId;
        }
        else
        {
            project.OnerProjectId = null;
        }

        if (command.ProjectStatusId is int statusId and > 0)
        {
            var status = await db.ProjectStatuses.FindAsync([statusId], cancellationToken).ConfigureAwait(false);
            if (status is null)
                return UpdateProjectResult.Fail("סטטוס הפרויקט לא נמצא.");
            project.ProjectStatusId = statusId;
        }

        project.PlaceId = place.Id;
        project.CompanyId = company.Id;
        project.ContactsId = contact.Id;
        project.ApproveDescription = string.IsNullOrWhiteSpace(command.ApproveDescription)
            ? null
            : command.ApproveDescription.Trim();
        project.Modified = DateTime.Now;

        var existingLinks = await db.TypeOfProjectInProjects
            .Where(t => t.ProjectId == project.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingBids = await db.Bids
            .Where(b => b.ProjectsId == project.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var selectedIds = selected.Select(s => s.JobTypeId).ToHashSet();
        var jobTypeTitles = await db.JobTypes
            .AsNoTracking()
            .Where(j => selectedIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Title, cancellationToken)
            .ConfigureAwait(false);
        if (jobTypeTitles.Count != selectedIds.Count)
            return UpdateProjectResult.Fail("אחד או יותר מסוגי הפרויקט אינם תקפים.");

        foreach (var line in selected)
        {
            var existing = existingLinks.FirstOrDefault(t => t.ProjectTypeId == line.JobTypeId);
            if (existing is null)
            {
                db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
                {
                    ProjectId = project.Id,
                    ProjectTypeId = line.JobTypeId,
                    Title = jobTypeTitles[line.JobTypeId],
                    AdminWorkerId = line.AdminWorkerId,
                    Created = DateTime.Now,
                    Modified = DateTime.Now,
                });
            }
            else
            {
                existing.AdminWorkerId = line.AdminWorkerId;
                existing.Modified = DateTime.Now;
            }

            var bid = existingBids.FirstOrDefault(b => b.JobTypeId == line.JobTypeId);
            if (bid is null)
            {
                db.Bids.Add(new Bid
                {
                    ProjectsId = project.Id,
                    JobTypeId = line.JobTypeId,
                    BidValue = line.BidValue,
                    BidSubmission = DateTime.Now,
                    Description = string.Empty,
                    Vat = 0,
                });
            }
            else
            {
                bid.BidValue = line.BidValue;
            }
        }

        var removedTypeIds = existingLinks
            .Where(t => t.ProjectTypeId is int id && !selectedIds.Contains(id))
            .Select(t => t.ProjectTypeId!.Value)
            .Distinct()
            .ToList();

        if (removedTypeIds.Count > 0)
        {
            var openStatuses = new[]
            {
                WorkflowStatus.Draft,
                WorkflowStatus.Active,
                WorkflowStatus.Paused,
            };
            var orphanCandidates = await db.WorkflowInstances
                .Where(i => i.ProjectId == project.Id
                    && i.JobTypeId != null
                    && removedTypeIds.Contains(i.JobTypeId.Value)
                    && openStatuses.Contains(i.Status))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var utcNow = DateTime.UtcNow;
            foreach (var instance in orphanCandidates)
            {
                instance.Notes = WorkflowOrphanTrackMarkers.PrependMarker(
                    instance.Notes,
                    instance.JobTypeId!.Value,
                    utcNow);
            }
        }

        foreach (var link in existingLinks.Where(t => t.ProjectTypeId is int id && !selectedIds.Contains(id)))
        {
            db.TypeOfProjectInProjects.Remove(link);
        }

        foreach (var bid in existingBids.Where(b => !selectedIds.Contains(b.JobTypeId)))
        {
            db.Bids.Remove(bid);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UpdateProjectResult.Ok();
    }

    private static string StatusLabel(WorkflowStatus status) => status switch
    {
        WorkflowStatus.Draft => "טיוטה",
        WorkflowStatus.Active => "פעיל",
        WorkflowStatus.Paused => "מושהה",
        WorkflowStatus.Completed => "הושלם",
        WorkflowStatus.Cancelled => "בוטל",
        _ => status.ToString(),
    };
}
