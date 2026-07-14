using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNet.Application.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Projects;

internal sealed class SqlProjectCreateService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IProjectFolderBootstrapper? folderBootstrapper = null,
    ILogger<SqlProjectCreateService>? logger = null) : IProjectCreateService
{
    public const int MaxTitleLength = 24;
    public const string DefaultQuoteStatusTitle = "איסוף חומר להצעת מחיר";
    public const string DefaultJobTypeTitle = "חומר כללי";
    public const int LegacyDefaultJobTypeId = 9;

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IProjectFolderBootstrapper? _folderBootstrapper = folderBootstrapper;
    private readonly ILogger<SqlProjectCreateService>? _logger = logger;

    public async Task<decimal> GetNextProjectNumberAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (!db.Database.IsRelational())
        {
            var maxId = await db.Projects.AnyAsync(cancellationToken).ConfigureAwait(false)
                ? await db.Projects.MaxAsync(p => p.Id, cancellationToken).ConfigureAwait(false)
                : 0;
            return maxId + 1;
        }

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IDENT_CURRENT('Projects') + IDENT_INCR('Projects') AS NextId";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 1 : Convert.ToDecimal(result);
    }

    public async Task<bool> ProjectNameExistsAsync(string projectName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return false;
        }

        var trimmed = projectName.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Title == trimmed, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CreateProjectResult> CreateAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var title = command.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return CreateProjectResult.Fail("יש להזין שם פרויקט.");
        }

        if (title.Length > MaxTitleLength)
        {
            return CreateProjectResult.Fail($"שם הפרויקט לא יכול לעלות על {MaxTitleLength} תווים.");
        }

        if (command.PlaceId <= 0 || command.CompanyId <= 0 || command.ContactId <= 0)
        {
            return CreateProjectResult.Fail("יש לבחור מקום, חברה ואיש קשר.");
        }

        var jobTypeIds = (command.JobTypeIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (jobTypeIds.Count == 0)
        {
            return CreateProjectResult.Fail("יש לבחור לפחות סוג פרויקט אחד.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (await db.Projects.AnyAsync(p => p.Title == title, cancellationToken).ConfigureAwait(false))
        {
            return CreateProjectResult.Fail("שם הפרויקט כבר קיים במערכת.");
        }

        var place = await db.Places.FindAsync([command.PlaceId], cancellationToken).ConfigureAwait(false);
        var company = await db.Companies.FindAsync([command.CompanyId], cancellationToken).ConfigureAwait(false);
        var contact = await db.Contacts.FindAsync([command.ContactId], cancellationToken).ConfigureAwait(false);
        if (place is null || company is null || contact is null)
        {
            return CreateProjectResult.Fail("מקום, חברה או איש קשר לא נמצאו.");
        }

        if (contact.CompanyId is int contactCompanyId && contactCompanyId != command.CompanyId)
        {
            return CreateProjectResult.Fail("איש הקשר אינו שייך לחברה שנבחרה.");
        }

        Project? parent = null;
        if (command.ParentProjectId is int parentId and > 0)
        {
            parent = await db.Projects.FindAsync([parentId], cancellationToken).ConfigureAwait(false);
            if (parent is null)
            {
                return CreateProjectResult.Fail("פרויקט האב לא נמצא.");
            }
        }

        var jobTypes = await db.JobTypes
            .Where(jt => jobTypeIds.Contains(jt.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (jobTypes.Count != jobTypeIds.Count)
        {
            return CreateProjectResult.Fail("אחד או יותר מסוגי הפרויקט אינם תקפים.");
        }

        var project = new Project
        {
            Title = title,
            PlaceId = place.Id,
            Place = place,
            CompanyId = company.Id,
            Company = company,
            ContactsId = contact.Id,
            Contacts = contact,
            OnerProjectId = parent?.Id,
            OnerProject = parent,
            Created = DateTime.Now,
            Modified = DateTime.Now,
        };

        var defaultStatus = await db.ProjectStatuses
            .FirstOrDefaultAsync(s => s.Title == DefaultQuoteStatusTitle, cancellationToken)
            .ConfigureAwait(false);
        if (defaultStatus is not null)
        {
            project.ProjectStatus = defaultStatus;
            project.ProjectStatusId = defaultStatus.Id;
        }

        // Keep the project row, its job-type rows, and the optional email link atomic so a mid-way
        // failure cannot leave an orphan project without job types or a half-linked email. Transactions
        // are relational-only (the EF InMemory provider used by unit tests does not support them).
        var useTransaction = db.Database.IsRelational();
        await using var transaction = useTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var jobType in jobTypes)
        {
            db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
            {
                ProjectId = project.Id,
                ProjectTypeId = jobType.Id,
                Title = jobType.Title,
                Created = DateTime.Now,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (command.EmailMessageId is int emailId and > 0)
        {
            var email = await db.EmailInboxMessages.FindAsync([emailId], cancellationToken).ConfigureAwait(false);
            if (email is not null)
            {
                email.ProjectId = project.Id;
                email.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Filesystem side effect runs only after the DB state is durable; best-effort (a folder
        // failure must not roll back a successfully-created project).
        try
        {
            _folderBootstrapper?.CreateFolders(project.Id);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SqlProjectCreate] Failed to create folders for Project {ProjectId}", project.Id);
        }

        return CreateProjectResult.Ok(project.Id, title, place.Title);
    }
}
