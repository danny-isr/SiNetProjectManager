using SiNet.Application.Abstractions.Email;
using SiNet.Application.Projects;

namespace SiNet.Application.Email;

/// <summary>
/// Loads the signed-in mailbox’s user labels and SiNet projects, then runs
/// <see cref="GmailMailboxLabelAuditMatcher"/> (DEV-026).
/// </summary>
public sealed class GmailMailboxLabelAuditService(
    IEmailGateway emailGateway,
    IProjectQueryService projectQuery,
    IPlaceCatalogService? placeCatalog = null) : IGmailMailboxLabelAuditService
{
    private readonly IEmailGateway _emailGateway =
        emailGateway ?? throw new ArgumentNullException(nameof(emailGateway));
    private readonly IProjectQueryService _projectQuery =
        projectQuery ?? throw new ArgumentNullException(nameof(projectQuery));
    private readonly IPlaceCatalogService? _placeCatalog = placeCatalog;

    public async Task<IReadOnlyList<GmailMailboxLabelAuditRow>> AuditAsync(
        CancellationToken cancellationToken = default)
    {
        var labels = await _emailGateway.GetAllUserLabelsAsync(cancellationToken).ConfigureAwait(false);
        var projects = await _projectQuery
            .SearchProjectsAsync(new ProjectSearchQuery(IncludeClosed: true), cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<string>? placeTitles = null;
        if (_placeCatalog is not null)
        {
            var places = await _placeCatalog.ListAsync(cancellationToken).ConfigureAwait(false);
            placeTitles = places.Select(static p => p.Title).ToArray();
        }

        return GmailMailboxLabelAuditMatcher.BuildRows(
            labels,
            projects,
            placeTitles,
            EmailGmailLabelNames.RootLabel);
    }
}
