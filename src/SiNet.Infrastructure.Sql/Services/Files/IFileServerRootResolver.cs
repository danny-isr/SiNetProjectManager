using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Resolves the FileServer root directory for a given project. Default implementation reproduces the
/// legacy <c>Project.ProjectFullPhate</c> formula: <c>MAPDrive + Place.Title(sanitized) + "\\" +
/// NameAndNumber(sanitized)</c>. Tests can substitute a temp-directory implementation.
/// </summary>
public interface IFileServerRootResolver
{
    Task<string?> ResolveAsync(SiNetSQLDbContext db, int projectId, CancellationToken ct = default);
}

/// <summary>
/// Default resolver. Native port of the legacy <c>ProjectPathBuilder</c> — same formula and
/// sanitization rules (mapped drive <c>U:\</c> + <c>FixDirectoryName</c>). Returns <c>null</c> when
/// the project has no <c>Place</c> or a blank <c>NameAndNumber</c>.
/// </summary>
public sealed class FileServerRootResolver : IFileServerRootResolver
{
    // Legacy ImpersonationFolderEdit.MAPDrive — the U:\ mapped drive to \\SI-WIN-2K19\SiProjects\.
    private const string MapDrive = "U:\\";

    public async Task<string?> ResolveAsync(SiNetSQLDbContext db, int projectId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Place)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        return BuildProjectFullPath(project);
    }

    /// <summary>Native equivalent of the legacy <c>Project.GetProjectFullPath()</c> extension.</summary>
    internal static string? BuildProjectFullPath(Project? project)
    {
        if (project?.Place == null || string.IsNullOrWhiteSpace(project.NameAndNumber))
            return null;

        var basePath = MapDrive + FixDirectoryName(project.Place.Title) + "\\";
        return basePath + FixDirectoryName(project.NameAndNumber);
    }

    // Legacy DataFunc.FixDirectoryName + RemovUnElodChrInDirectoryName, ported verbatim.
    private static string? FixDirectoryName(string? nameVal)
    {
        if (string.IsNullOrEmpty(nameVal))
            return null;

        return RemoveInvalidDirectoryChars(
                nameVal.Trim()
                       .Replace("    ", " ")
                       .Replace("   ", " ")
                       .Replace("  ", " "))
            ?.Replace(" ", "_");
    }

    private static string? RemoveInvalidDirectoryChars(string? nameVal)
    {
        if (string.IsNullOrEmpty(nameVal))
            return null;

        return nameVal.Trim()
                      .Replace("\\", "")
                      .Replace("/", "")
                      .Replace("\"", "")
                      .Replace(":", "")
                      .Replace("*", "")
                      .Replace("?", "")
                      .Replace("<", "")
                      .Replace(">", "")
                      .Replace("|", "");
    }
}
