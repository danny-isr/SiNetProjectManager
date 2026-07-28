using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.MasterPlan;

/// <summary>
/// Native MasterPlan company/contact mapping over SiData EF + Replica MP_* tables.
/// </summary>
public sealed class SqlMasterPlanMappingService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IMasterPlanEmployeeConnectionProvider connectionProvider) : IMasterPlanMappingService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IMasterPlanEmployeeConnectionProvider _connectionProvider =
        connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));

    public async Task<MasterPlanMappingLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var companies = await db.Companies.AsNoTracking()
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.Email,
                c.WorkPhone,
                c.MasterPlanCompanyId,
                c.IsActive,
                ProjectCount = c.Projects.Count,
                ContactCount = c.Contacts.Count,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var contacts = await db.Contacts.AsNoTracking()
            .Select(c => new
            {
                c.Id,
                c.FullName,
                c.CompanyId,
                CompanyTitle = c.Company != null ? c.Company.Title : null,
                c.Email,
                c.WorkPhone,
                c.CellPhone,
                c.MasterPlanContactId,
                c.IsActive,
                ProjectCount = c.Projects.Count,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string? warning = null;
        IReadOnlyList<MpCompanyOptionDto> mpCompanies = [];
        IReadOnlyList<MpContactOptionDto> mpContacts = [];

        var replica = NormalizeReplicaConnectionString(
            _connectionProvider.GetConnectionSettings().ReplicaDatabase);
        if (string.IsNullOrWhiteSpace(replica))
        {
            warning = "אין Connection String ל-Replica ב-Vault — מיפוי MasterPlan יוצג ללא אפשרויות MP.";
        }
        else
        {
            try
            {
                (mpCompanies, mpContacts) = await LoadReplicaOptionsAsync(replica, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                warning = $"טעינת Replica נכשלה: {ex.Message}";
            }
        }

        var companyDtos = companies
            .Select(c => new MasterPlanCompanyMappingDto(
                c.Id,
                c.Title ?? string.Empty,
                c.Email,
                c.WorkPhone,
                c.ProjectCount,
                c.ContactCount,
                c.IsActive,
                c.MasterPlanCompanyId,
                MatchStatus: c.MasterPlanCompanyId is null ? null : "קיים",
                IsAutoMatch: false))
            .OrderBy(c => c.SiNetTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var contactDtos = contacts
            .Select(c => new MasterPlanContactMappingDto(
                c.Id,
                c.FullName ?? string.Empty,
                c.CompanyId,
                c.CompanyTitle,
                c.Email,
                c.WorkPhone ?? c.CellPhone,
                c.ProjectCount,
                c.IsActive,
                c.MasterPlanContactId,
                MatchStatus: c.MasterPlanContactId is null ? null : "קיים",
                IsAutoMatch: false))
            .OrderBy(c => c.SiNetFullName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new MasterPlanMappingLoadResult(companyDtos, contactDtos, mpCompanies, mpContacts, warning);
    }

    public async Task<MasterPlanMappingApplyResult> ApplyAsync(
        MasterPlanMappingApplyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var companyIds = command.Companies.Select(c => c.SiNetId).Distinct().ToList();
        var contactIds = command.Contacts.Select(c => c.SiNetId).Distinct().ToList();

        var companies = await db.Companies
            .Where(c => companyIds.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var contacts = await db.Contacts
            .Where(c => contactIds.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var companyById = companies.ToDictionary(c => c.Id);
        var contactById = contacts.ToDictionary(c => c.Id);
        var companiesUpdated = 0;
        var contactsUpdated = 0;

        foreach (var change in command.Companies)
        {
            if (!companyById.TryGetValue(change.SiNetId, out var company))
            {
                continue;
            }

            company.MasterPlanCompanyId = change.MasterPlanCompanyId;
            company.IsActive = change.IsActive;
            company.Modified = DateTime.UtcNow;
            companiesUpdated++;
        }

        foreach (var change in command.Contacts)
        {
            if (!contactById.TryGetValue(change.SiNetId, out var contact))
            {
                continue;
            }

            contact.MasterPlanContactId = change.MasterPlanContactId;
            contact.IsActive = change.IsActive;
            contact.Modified = DateTime.UtcNow;
            contactsUpdated++;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return new MasterPlanMappingApplyResult(
                false,
                "שמירה נכשלה — מזהה MasterPlan כבר משויך לרשומה אחרת.",
                0,
                0);
        }

        return new MasterPlanMappingApplyResult(
            true,
            $"נשמרו {companiesUpdated} חברות ו-{contactsUpdated} אנשי קשר.",
            companiesUpdated,
            contactsUpdated);
    }

    public async Task<MasterPlanCompleteMissingResult> CompleteMissingAsync(
        CancellationToken cancellationToken = default)
    {
        var load = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var mappedCompanyIds = load.Companies
            .Where(c => c.MasterPlanCompanyId is not null)
            .Select(c => c.MasterPlanCompanyId!.Value)
            .ToHashSet();
        var mappedContactIds = load.Contacts
            .Where(c => c.MasterPlanContactId is not null)
            .Select(c => c.MasterPlanContactId!.Value)
            .ToHashSet();

        var missingCompanies = load.MpCompanies.Where(c => !mappedCompanyIds.Contains(c.Id)).ToList();
        var missingContacts = load.MpContacts.Where(c => !mappedContactIds.Contains(c.Id)).ToList();

        if (missingCompanies.Count == 0 && missingContacts.Count == 0)
        {
            return new MasterPlanCompleteMissingResult(true, "אין רשומות MasterPlan חסרות ב-SiNet.", 0, 0);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        foreach (var mp in missingCompanies)
        {
            db.Companies.Add(new Company
            {
                Title = Truncate(mp.Name, 255) ?? "ללא שם",
                Email = Truncate(mp.Email, 255),
                WorkPhone = Truncate(mp.Phone, 50),
                WorkAddress = Truncate(mp.Address, 255),
                WorkCity = Truncate(mp.City, 100),
                RegistrationNumber = Truncate(mp.RegistrationNumber, 50),
                MasterPlanCompanyId = mp.Id,
                MasterPlanSync = true,
                Comments = "מקור: MasterPlan",
                IsActive = true,
                Created = now,
                Modified = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var companyLookup = await db.Companies.AsNoTracking()
            .Where(c => c.MasterPlanCompanyId != null)
            .Select(c => new { c.Id, c.MasterPlanCompanyId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var siNetCompanyByMp = companyLookup
            .Where(c => c.MasterPlanCompanyId is not null)
            .ToDictionary(c => c.MasterPlanCompanyId!.Value, c => c.Id);

        var contactsCreated = 0;
        foreach (var mp in missingContacts)
        {
            int? companyId = null;
            if (mp.CompanyId is int mpCompanyId
                && siNetCompanyByMp.TryGetValue(mpCompanyId, out var mapped))
            {
                companyId = mapped;
            }

            db.Contacts.Add(new Contact
            {
                FirstName = Truncate(mp.FirstName, 100),
                FullName = Truncate(mp.FullName, 255) ?? mp.FullName,
                Email = Truncate(mp.Email, 255),
                WorkPhone = Truncate(mp.Phone, 50),
                CellPhone = Truncate(mp.Mobile, 50),
                WorkAddress = Truncate(mp.Address, 255),
                CompanyId = companyId,
                MasterPlanContactId = mp.Id,
                MasterPlanSync = true,
                Comments = "מקור: MasterPlan",
                IsActive = true,
                Created = now,
                Modified = now,
            });
            contactsCreated++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new MasterPlanCompleteMissingResult(
            true,
            $"נוצרו {missingCompanies.Count} חברות ו-{contactsCreated} אנשי קשר מ-MasterPlan.",
            missingCompanies.Count,
            contactsCreated);
    }

    public async Task<MasterPlanEnableFullSyncResult> EnableFullSyncAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var companiesUpdated = await db.Database.ExecuteSqlRawAsync(
            sql: @"UPDATE Company SET MasterPlanSync = 1
                   WHERE MasterPlanCompanyId IS NOT NULL AND MasterPlanSync = 0",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var contactsUpdated = await db.Database.ExecuteSqlRawAsync(
            sql: @"UPDATE Contacts SET MasterPlanSync = 1
                   WHERE MasterPlanContactId IS NOT NULL AND MasterPlanSync = 0",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new MasterPlanEnableFullSyncResult(
            true,
            $"סומן Full Sync ל-{companiesUpdated} חברות ו-{contactsUpdated} אנשי קשר.",
            companiesUpdated,
            contactsUpdated);
    }

    private static async Task<(IReadOnlyList<MpCompanyOptionDto> Companies, IReadOnlyList<MpContactOptionDto> Contacts)>
        LoadReplicaOptionsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var companies = new List<MpCompanyOptionDto>();
        await using (var command = new SqlCommand(
            """
            SELECT ID, Name, Email, PhoneNum, RegistrationNumber, Address, City
            FROM MP_Companies
            ORDER BY Name
            """,
            connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                companies.Add(new MpCompanyOptionDto(
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim(),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        var contacts = new List<MpContactOptionDto>();
        await using (var command = new SqlCommand(
            """
            SELECT ID, FirstName, LastName, CompanyName, Email, Phone, Mobile, CompanyID, Address
            FROM MP_Contacts
            ORDER BY FirstName, LastName
            """,
            connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var first = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                var last = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim();
                var fullName = $"{first} {last}".Trim();
                contacts.Add(new MpContactOptionDto(
                    reader.GetInt32(0),
                    first,
                    fullName,
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        return (companies, contacts);
    }

    private static string? NormalizeReplicaConnectionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cs = raw.Trim().Replace("\\\\", "\\", StringComparison.Ordinal);
        if (!cs.Contains("TrustServerCertificate", StringComparison.OrdinalIgnoreCase))
        {
            cs = cs.TrimEnd(';') + ";TrustServerCertificate=true";
        }

        return cs;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && (sql.Number == 2601 || sql.Number == 2627))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
