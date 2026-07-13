using Microsoft.EntityFrameworkCore;
using SiNet.Application.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Projects;

internal sealed class SqlPlaceCatalogService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IPlaceCatalogService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<IReadOnlyList<PlaceDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Places
            .AsNoTracking()
            .OrderBy(p => p.Title)
            .Select(p => new PlaceDto(p.Id, p.Title ?? string.Empty, p.CityIcon, p.InUse != false))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PlaceDto> SaveAsync(PlaceDto place, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(place);
        var title = place.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("יש להזין שם מקום.", nameof(place));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        Place entity;
        if (place.Id > 0)
        {
            entity = await db.Places.FindAsync([place.Id], cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException($"מקום {place.Id} לא נמצא.");
            entity.Title = title;
            entity.CityIcon = place.CityIcon;
            entity.InUse = place.InUse;
            entity.Modified = DateTime.Now;
        }
        else
        {
            entity = new Place
            {
                Title = title,
                CityIcon = place.CityIcon,
                InUse = place.InUse,
                Created = DateTime.Now,
            };
            db.Places.Add(entity);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PlaceDto(entity.Id, entity.Title ?? title, entity.CityIcon, entity.InUse != false);
    }
}

internal sealed class SqlCompanyCatalogService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : ICompanyCatalogService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<IReadOnlyList<CompanyDto>> ListCompaniesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Companies
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Title)
            .Select(c => new CompanyDto(c.Id, c.Title ?? string.Empty, c.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ContactDto>> ListContactsAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Contacts
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .OrderBy(c => c.FullName ?? c.Title)
            .Select(c => new ContactDto(
                c.Id,
                companyId,
                c.FullName ?? c.Title ?? $"איש קשר {c.Id}"))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CompanyDto> AddCompanyAsync(string title, CancellationToken cancellationToken = default)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("יש להזין שם חברה.", nameof(title));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var company = new Company
        {
            Title = trimmed,
            IsActive = true,
            Created = DateTime.Now,
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CompanyDto(company.Id, trimmed, true);
    }

    public async Task<ContactDto> AddContactAsync(
        int companyId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (companyId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(companyId));
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("יש להזין שם איש קשר.", nameof(displayName));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var companyExists = await db.Companies.AnyAsync(c => c.Id == companyId, cancellationToken)
            .ConfigureAwait(false);
        if (!companyExists)
        {
            throw new InvalidOperationException($"חברה {companyId} לא נמצאה.");
        }

        var contact = new Contact
        {
            CompanyId = companyId,
            FullName = trimmed,
            Title = trimmed,
            IsActive = true,
            Created = DateTime.Now,
        };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ContactDto(contact.Id, companyId, trimmed);
    }
}

internal sealed class SqlJobTypeQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IJobTypeQueryService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<IReadOnlyList<JobTypeDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.JobTypes
            .AsNoTracking()
            .OrderBy(j => j.Title)
            .Select(j => new JobTypeDto(j.Id, j.Title ?? string.Empty))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int?> ResolveDefaultJobTypeIdAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var byId = await db.JobTypes
            .AsNoTracking()
            .Where(j => j.Id == SqlProjectCreateService.LegacyDefaultJobTypeId)
            .Select(j => (int?)j.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (byId is > 0)
        {
            return byId;
        }

        return await db.JobTypes
            .AsNoTracking()
            .Where(j => j.Title == SqlProjectCreateService.DefaultJobTypeTitle)
            .Select(j => (int?)j.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
