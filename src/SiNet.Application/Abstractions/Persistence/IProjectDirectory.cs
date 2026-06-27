using SiNet.Domain.Identifiers;

namespace SiNet.Application.Abstractions.Persistence;

/// <summary>
/// Read access to project records. Implemented by <c>SiNet.Infrastructure.Sql</c> using
/// <c>IDbContextFactory&lt;&gt;</c> over the existing SiNetSQL context — the UI must never
/// touch the DbContext directly.
/// </summary>
public interface IProjectDirectory
{
    Task<IReadOnlyList<ProjectSummary>> GetActiveProjectsAsync(CancellationToken cancellationToken = default);

    Task<ProjectSummary?> GetByIdAsync(ProjectId id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Application-level projection of a project row (no EF types leak past this boundary).
/// </summary>
public sealed record ProjectSummary(ProjectId Id, string Number, string Name, string Status);
