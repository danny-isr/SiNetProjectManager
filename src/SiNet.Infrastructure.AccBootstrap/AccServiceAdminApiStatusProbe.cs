using Microsoft.EntityFrameworkCore;
using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Infrastructure.Autodesk;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.AccBootstrap;

/// <summary>
/// Resolves the default ACC account/hub from SQL and probes Autodesk Admin API
/// with the AccService Admin 3-legged token.
/// </summary>
public sealed class AccServiceAdminApiStatusProbe(
    ITokenProvider tokenProvider,
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory) : IAccServiceAdminApiStatusProbe
{
    private readonly ITokenProvider _tokenProvider =
        tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));

    public async Task<string> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (_tokenProvider.TokenStorePurpose != AutodeskTokenStorePurpose.AccServiceAdmin)
        {
            return "unavailable:wrong-token-purpose";
        }

        string? hubId;
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var hub = await db.AccHubs.AsNoTracking()
                .FirstOrDefaultAsync(h => h.IsDefault, cancellationToken)
                .ConfigureAwait(false)
                ?? await db.AccHubs.AsNoTracking()
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            hubId = hub?.HubId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"unavailable:hub:{ex.GetType().Name}";
        }

        return await AccServiceAdminApiProbe
            .ProbeListProjectsAsync(_tokenProvider, hubId ?? string.Empty, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
