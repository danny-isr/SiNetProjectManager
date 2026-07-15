using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Resolves the folder-path segments (root → leaf) for a given <c>ProjectFile</c> by walking up the
/// <c>ProjectFolder</c> hierarchy. The root container folder (no parent or self-parent) is excluded.
/// </summary>
public interface IFolderPathResolver
{
    Task<IReadOnlyList<string>> ResolveAsync(
        SiNetSQLDbContext db,
        int projectFileId,
        CancellationToken ct = default);
}
