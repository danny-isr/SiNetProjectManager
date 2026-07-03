using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class Bim360AccLiveProjectReader(ITokenProvider? tokenProvider) : IAccLiveProjectReader
{
    private readonly ITokenProvider? _tokenProvider = tokenProvider;

    public async Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(
        string hubId,
        CancellationToken cancellationToken = default)
    {
        if (_tokenProvider is null || string.IsNullOrWhiteSpace(hubId))
        {
            return [];
        }

        var projects = await new Bim360Service(_tokenProvider)
            .ListAccNativeProjectsAsync(hubId.Trim(), cancellationToken)
            .ConfigureAwait(false);

        return projects
            .Where(static project => !string.IsNullOrWhiteSpace(project.Id))
            .Select(static project => new AccProjectCatalogEntry(
                project.Id.Trim(),
                string.IsNullOrWhiteSpace(project.Name) ? project.Id.Trim() : project.Name.Trim(),
                "LiveAcc"))
            .OrderBy(static project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static project => project.ProjectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
