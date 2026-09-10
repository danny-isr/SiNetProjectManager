using Microsoft.EntityFrameworkCore;
using SiNet.Application.Billing;
using SiNet.Application.Projects;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Billing;

public sealed class SqlBillingPreparationProjectMapper(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IBillingPreparationProjectMapper
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<int?> TryResolveSiNetProjectIdAsync(
        string? projectNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectNumber))
            return null;

        var wanted = projectNumber.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var candidates = await db.Projects
            .AsNoTracking()
            .Where(p => p.Number != null)
            .Select(p => new { p.Id, p.Number })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var match = candidates.FirstOrDefault(p =>
            string.Equals(ProjectNumberFormatting.Format(p.Number), wanted, StringComparison.OrdinalIgnoreCase));
        return match?.Id;
    }
}
