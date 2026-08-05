using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Projects;
using SiNet.Infrastructure.Sql.Services.Files;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Projects;

/// <summary>
/// Rename checklist (DEV-008 Layer A): ACC Docs → Drive → FileServer → DB Title.
/// Shared stores first so a local FileServer move cannot race ahead of ACC (split-brain).
/// </summary>
internal sealed class ProjectRenameOrchestrator(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IProjectDriveRootRenameService? driveRootRename = null,
    IAccFolderRenameService? accFolderRename = null,
    ILogger<ProjectRenameOrchestrator>? logger = null) : IProjectRenameOrchestrator
{
    public const int MaxTitleLength = 24;

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly IProjectDriveRootRenameService? _driveRootRename = driveRootRename;
    private readonly IAccFolderRenameService? _accFolderRename = accFolderRename;
    private readonly ILogger<ProjectRenameOrchestrator>? _logger = logger;

    public async Task<ProjectRenameAnalysis> AnalyzeAsync(
        int projectId,
        string newTitle,
        CancellationToken cancellationToken = default)
    {
        var desired = newTitle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(desired))
        {
            return Cannot(projectId, string.Empty, string.Empty, string.Empty, string.Empty,
                "יש להזין שם פרויקט חדש.");
        }

        if (desired.Length > MaxTitleLength)
        {
            return Cannot(projectId, string.Empty, desired, string.Empty, string.Empty,
                $"שם הפרויקט לא יכול לעלות על {MaxTitleLength} תווים.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Place)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return Cannot(projectId, string.Empty, desired, string.Empty, string.Empty, "הפרויקט לא נמצא.");
        }

        var currentTitle = project.Title?.Trim() ?? string.Empty;
        if (string.Equals(currentTitle, desired, StringComparison.Ordinal))
        {
            return Cannot(projectId, currentTitle, desired,
                project.NameAndNumber ?? string.Empty,
                project.NameAndNumber ?? string.Empty,
                "השם החדש זהה לשם הנוכחי.");
        }

        if (await db.Projects.AsNoTracking()
                .AnyAsync(p => p.Id != projectId && p.Title == desired, cancellationToken)
                .ConfigureAwait(false))
        {
            return Cannot(projectId, currentTitle, desired,
                project.NameAndNumber ?? string.Empty,
                string.Empty,
                "שם הפרויקט כבר קיים במערכת.");
        }

        var number = (int)(project.Number ?? project.Id);
        var currentNan = project.NameAndNumber
            ?? ProjectFolderNameHelper.BuildNameAndNumber(number, currentTitle);
        var predictedNan = ProjectFolderNameHelper.BuildNameAndNumber(number, desired);

        var oldFs = FileServerRootResolver.BuildProjectFullPath(project);
        var newFs = BuildPredictedFileServerPath(project, predictedNan);

        var mapping = await db.ProjectAccMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);

        var oldFolderName = ProjectFolderNameHelper.FixDirectoryName(currentNan) ?? currentNan;
        var newFolderName = ProjectFolderNameHelper.FixDirectoryName(predictedNan) ?? predictedNan;

        // Display order matches execute order: ACC → Drive → FileServer → DB.
        var steps = new List<ProjectRenameStepPlan>
        {
            new(
                ProjectRenameStepKind.AccDocs,
                mapping is null || string.IsNullOrWhiteSpace(mapping.AccTargetFolderId)
                    ? "ACC Docs: אין מיפוי — ידולג"
                    : string.Equals(oldFolderName, newFolderName, StringComparison.Ordinal)
                        ? "ACC Docs: שם התיקייה ללא שינוי — ידולג"
                        : $"ACC Docs: שינוי שם תיקייה '{oldFolderName}' → '{newFolderName}' (מזהה {mapping.AccTargetFolderId})",
                mapping?.AccTargetFolderId,
                newFolderName),
            new(
                ProjectRenameStepKind.GoogleDrive,
                $"Google Drive: שינוי שם שורש תחת ProjectsRoot '{oldFolderName}' → '{newFolderName}'",
                oldFolderName,
                newFolderName),
            new(
                ProjectRenameStepKind.FileServer,
                Directory.Exists(oldFs ?? string.Empty)
                    ? $"העברת תיקיית FileServer: {oldFs} → {newFs}"
                    : $"יצירת תיקיית FileServer: {newFs}",
                oldFs,
                newFs),
            new(
                ProjectRenameStepKind.Database,
                $"DB: עדכון Title ל־'{desired}' (NameAndNumber via trigger)",
                currentTitle,
                desired),
        };

        return new ProjectRenameAnalysis(
            projectId,
            currentTitle,
            desired,
            currentNan,
            predictedNan,
            CanExecute: true,
            ReasonIfCannot: null,
            steps);
    }

    public async Task<ProjectRenameExecuteResult> ExecuteAsync(
        ProjectRenameAnalysis analysis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (!analysis.CanExecute)
        {
            return new ProjectRenameExecuteResult(
                false,
                [],
                analysis.ReasonIfCannot ?? "לא ניתן לבצע שינוי שם.");
        }

        var results = new List<ProjectRenameStepResult>();
        var oldFolderName = ProjectFolderNameHelper.FixDirectoryName(analysis.CurrentNameAndNumber)
            ?? analysis.CurrentNameAndNumber;
        var newFolderName = ProjectFolderNameHelper.FixDirectoryName(analysis.PredictedNameAndNumber)
            ?? analysis.PredictedNameAndNumber;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var mapping = await db.ProjectAccMappings
            .FirstOrDefaultAsync(m => m.ProjectId == analysis.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        // 1. ACC first — shared store; keeps FileServer from racing ahead on failure.
        if (mapping is null || string.IsNullOrWhiteSpace(mapping.AccTargetFolderId))
        {
            results.Add(Skip(ProjectRenameStepKind.AccDocs, "אין מיפוי ACC — דולג."));
        }
        else if (string.Equals(oldFolderName, newFolderName, StringComparison.Ordinal))
        {
            results.Add(Skip(ProjectRenameStepKind.AccDocs, "שם תיקיית ACC ללא שינוי — דולג."));
        }
        else if (string.IsNullOrWhiteSpace(mapping.AccProjectId))
        {
            results.Add(Fail(ProjectRenameStepKind.AccDocs, "יש מיפוי תיקייה אך חסר AccProjectId."));
            return Stop(results, "ACC Docs נכשל — DB ו-FileServer לא עודכנו.");
        }
        else if (_accFolderRename is null)
        {
            results.Add(Fail(
                ProjectRenameStepKind.AccDocs,
                "שירות שינוי שם ACC לא רשום."));
            return Stop(results, "ACC Docs נכשל — DB ו-FileServer לא עודכנו.");
        }
        else
        {
            try
            {
                var accOutcome = await _accFolderRename
                    .RenameFolderAsync(
                        mapping.AccProjectId,
                        mapping.AccTargetFolderId,
                        newFolderName,
                        cancellationToken)
                    .ConfigureAwait(false);

                results.Add(accOutcome.Status switch
                {
                    AccFolderRenameStatus.Succeeded =>
                        Ok(ProjectRenameStepKind.AccDocs, accOutcome.Message),
                    AccFolderRenameStatus.Skipped =>
                        Skip(ProjectRenameStepKind.AccDocs, accOutcome.Message),
                    _ => Fail(ProjectRenameStepKind.AccDocs, accOutcome.Message),
                });

                if (accOutcome.Status == AccFolderRenameStatus.Failed)
                    return Stop(results, "ACC Docs נכשל — DB ו-FileServer לא עודכנו.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ProjectRename] ACC failed for project {ProjectId}", analysis.ProjectId);
                results.Add(Fail(ProjectRenameStepKind.AccDocs, ex.Message));
                return Stop(results, "ACC Docs נכשל — DB ו-FileServer לא עודכנו.");
            }
        }

        // 2. Drive
        try
        {
            if (_driveRootRename is null)
            {
                results.Add(Skip(ProjectRenameStepKind.GoogleDrive, "שירות Drive לא רשום — דולג."));
            }
            else
            {
                var driveOutcome = await _driveRootRename
                    .RenameRootAsync(oldFolderName, newFolderName, cancellationToken)
                    .ConfigureAwait(false);
                results.Add(driveOutcome.Status switch
                {
                    ProjectDriveRootRenameStatus.Succeeded =>
                        Ok(ProjectRenameStepKind.GoogleDrive, driveOutcome.Message),
                    ProjectDriveRootRenameStatus.Skipped =>
                        Skip(ProjectRenameStepKind.GoogleDrive, driveOutcome.Message),
                    _ => Fail(ProjectRenameStepKind.GoogleDrive, driveOutcome.Message),
                });
                if (driveOutcome.Status == ProjectDriveRootRenameStatus.Failed)
                    return Stop(results, "Google Drive נכשל — DB ו-FileServer לא עודכנו. " +
                        "אם ACC כבר שונה — נדרש תיקון ידני ב-Drive או rollback ב-ACC.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ProjectRename] Drive failed for project {ProjectId}", analysis.ProjectId);
            results.Add(Fail(ProjectRenameStepKind.GoogleDrive, ex.Message));
            return Stop(results, "Google Drive נכשל — DB ו-FileServer לא עודכנו.");
        }

        // 3. FileServer (after shared stores)
        var fsStep = analysis.Steps.First(s => s.Kind == ProjectRenameStepKind.FileServer);
        try
        {
            var oldPath = fsStep.SourcePathOrId;
            var newPath = fsStep.TargetPathOrName;
            if (string.IsNullOrWhiteSpace(newPath))
            {
                results.Add(Fail(ProjectRenameStepKind.FileServer, "נתיב FileServer יעד ריק."));
                return Stop(results, "FileServer נכשל — DB לא עודכן. אחסון משותף כבר עודכן — נדרש תיקון ידני.");
            }

            if (!string.IsNullOrWhiteSpace(oldPath) && Directory.Exists(oldPath))
            {
                if (Directory.Exists(newPath))
                {
                    results.Add(Fail(ProjectRenameStepKind.FileServer, $"תיקיית יעד כבר קיימת: {newPath}"));
                    return Stop(results, "FileServer נכשל — DB לא עודכן. אחסון משותף כבר עודכן — נדרש תיקון ידני.");
                }

                var parent = Path.GetDirectoryName(newPath);
                if (!string.IsNullOrWhiteSpace(parent) && !Directory.Exists(parent))
                    Directory.CreateDirectory(parent);

                Directory.Move(oldPath, newPath);
                results.Add(Ok(ProjectRenameStepKind.FileServer, $"הועבר: {oldPath} → {newPath}"));
            }
            else
            {
                Directory.CreateDirectory(newPath);
                results.Add(Ok(ProjectRenameStepKind.FileServer, $"נוצרה תיקייה: {newPath}"));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ProjectRename] FileServer failed for project {ProjectId}", analysis.ProjectId);
            results.Add(Fail(ProjectRenameStepKind.FileServer, ex.Message));
            return Stop(results, "FileServer נכשל — DB לא עודכן. אחסון משותף כבר עודכן — נדרש תיקון ידני.");
        }

        // 4. DB Title last
        try
        {
            var project = await db.Projects
                .FirstOrDefaultAsync(p => p.Id == analysis.ProjectId, cancellationToken)
                .ConfigureAwait(false);
            if (project is null)
            {
                results.Add(Fail(ProjectRenameStepKind.Database, "הפרויקט לא נמצא בעת עדכון DB."));
                return Stop(results, "DB נכשל לאחר שינוי אחסון — נדרש תיקון ידני.");
            }

            project.Title = analysis.DesiredTitle;
            project.Modified = DateTime.Now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (mapping is not null && !string.IsNullOrWhiteSpace(mapping.AccTargetFolderPath))
            {
                mapping.AccTargetFolderPath = ReplaceLastPathSegment(
                    mapping.AccTargetFolderPath, newFolderName);
                mapping.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            results.Add(Ok(ProjectRenameStepKind.Database, $"Title עודכן ל־'{analysis.DesiredTitle}'."));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ProjectRename] DB Title failed for project {ProjectId}", analysis.ProjectId);
            results.Add(Fail(ProjectRenameStepKind.Database, ex.Message));
            return Stop(results, "DB נכשל לאחר שינוי אחסון — נדרש תיקון ידני.");
        }

        return new ProjectRenameExecuteResult(true, results);
    }

    private static string? BuildPredictedFileServerPath(Project project, string predictedNameAndNumber)
    {
        if (project.Place is null)
            return null;

        var clone = new Project
        {
            Id = project.Id,
            Title = project.Title,
            Number = project.Number,
            NameAndNumber = predictedNameAndNumber,
            Place = project.Place,
            PlaceId = project.PlaceId,
        };
        return FileServerRootResolver.BuildProjectFullPath(clone);
    }

    private static string ReplaceLastPathSegment(string path, string newSegment)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var slash = trimmed.LastIndexOfAny(['/', '\\']);
        if (slash < 0)
            return newSegment;
        return trimmed[..(slash + 1)] + newSegment;
    }

    private static ProjectRenameAnalysis Cannot(
        int projectId,
        string currentTitle,
        string desiredTitle,
        string currentNan,
        string predictedNan,
        string reason) =>
        new(projectId, currentTitle, desiredTitle, currentNan, predictedNan, false, reason, []);

    private static ProjectRenameStepResult Ok(ProjectRenameStepKind kind, string message) =>
        new(kind, ProjectRenameStepStatus.Succeeded, message);

    private static ProjectRenameStepResult Fail(ProjectRenameStepKind kind, string message) =>
        new(kind, ProjectRenameStepStatus.Failed, message);

    private static ProjectRenameStepResult Skip(ProjectRenameStepKind kind, string message) =>
        new(kind, ProjectRenameStepStatus.Skipped, message);

    private static ProjectRenameExecuteResult Stop(
        List<ProjectRenameStepResult> results,
        string error) =>
        new(false, results, error);
}
