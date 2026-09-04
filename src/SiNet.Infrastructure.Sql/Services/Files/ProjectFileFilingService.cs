using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Diagnostics;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Files;

/// <summary>
/// Native (Infrastructure.Sql) implementation of the centralized project-file filing service. Files
/// a single source file into a target <see cref="ProjectFile"/> slot, handling both FileServer and
/// ACC destinations. Native port of the legacy <c>SiNetSQL.Services.Files.ProjectFileFilingService</c>.
/// <para>
/// ACC writes go through the native <see cref="IAccFileUploadService"/> port. When
/// <see cref="IProjectAccMappingProvisioner"/> is available, a missing/incomplete
/// <c>ProjectAccMapping</c> triggers on-demand <c>EnsureMappingAsync</c> (same idea as legacy
/// optional AccProvisioning) before failing closed. Routing is driven exclusively by
/// <c>ProjectFile.StorageDestination</c> — no silent fallback between backends.
/// </para>
/// </summary>
public sealed class ProjectFileFilingService : IProjectFileFilingService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IFolderPathResolver _folderPathResolver;
    private readonly IFileServerMetadataStore _metadataStore;
    private readonly IFileServerVersionArchiver _versionArchiver;
    private readonly IFileServerRootResolver _fileServerRootResolver;
    private readonly IAccFileUploadService _accUploadService;
    private readonly IProjectAccMappingProvisioner? _accMappingProvisioner;
    private readonly IIdentityOperationGuard? _identityGuard;

    public ProjectFileFilingService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IFolderPathResolver folderPathResolver,
        IFileServerMetadataStore metadataStore,
        IFileServerVersionArchiver versionArchiver,
        IFileServerRootResolver fileServerRootResolver,
        IAccFileUploadService accUploadService,
        IProjectAccMappingProvisioner? accMappingProvisioner = null,
        IIdentityOperationGuard? identityGuard = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _folderPathResolver = folderPathResolver ?? throw new ArgumentNullException(nameof(folderPathResolver));
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        _versionArchiver = versionArchiver ?? throw new ArgumentNullException(nameof(versionArchiver));
        _fileServerRootResolver = fileServerRootResolver ?? throw new ArgumentNullException(nameof(fileServerRootResolver));
        _accUploadService = accUploadService ?? throw new ArgumentNullException(nameof(accUploadService));
        _accMappingProvisioner = accMappingProvisioner;
        _identityGuard = identityGuard;
    }

    public async Task<FileProjectFileResult> FileAsync(FileProjectFileRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourceLocalPath))
            throw new ArgumentException("SourceLocalPath is required.", nameof(request));
        if (!File.Exists(request.SourceLocalPath))
            throw new FileNotFoundException("Source file not found.", request.SourceLocalPath);
        if (string.IsNullOrWhiteSpace(request.OriginalFileName))
            throw new ArgumentException("OriginalFileName is required.", nameof(request));

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var projectFile = await db.ProjectFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(pf => pf.Id == request.ProjectFileId, ct)
            ?? throw new InvalidOperationException($"ProjectFile #{request.ProjectFileId} not found.");

        var project = await db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct)
            ?? throw new InvalidOperationException($"Project #{request.ProjectId} not found.");

        string? altName = null;
        if (request.ProjectAlternativeId is > 0)
        {
            altName = await db.ProjectAlternatives
                .AsNoTracking()
                .Where(a => a.Id == request.ProjectAlternativeId!.Value)
                .Select(a => a.Name)
                .FirstOrDefaultAsync(ct);
        }
        altName = string.IsNullOrWhiteSpace(altName) ? "1" : altName!;

        var conventionFileName = ProjectFileNameBuilder.Build(
            (int)(project.Number ?? 0),
            projectFile.TypeProjId ?? 0,
            (int)(projectFile.Number ?? 0),
            altName,
            !string.IsNullOrEmpty(request.FolderNameOverride) ? string.Empty : (projectFile.Title ?? string.Empty),
            request.OriginalFileName);

        var folderPath = await _folderPathResolver.ResolveAsync(db, request.ProjectFileId, ct);
        var finalFolderPath = folderPath.ToList();
        if (!string.IsNullOrEmpty(request.FolderNameOverride))
            finalFolderPath.Add(request.FolderNameOverride);

        return projectFile.StorageDestination switch
        {
            FileStorageDestination.Acc => await FileToAccAsync(db, request, conventionFileName, finalFolderPath, ct),
            FileStorageDestination.FileServer => await FileToFileServerAsync(db, request, conventionFileName, finalFolderPath, ct),
            FileStorageDestination.GoogleDrive => throw new NotSupportedException(
                "Filing to Google Drive is not wired through ProjectFileFilingService. No fallback is performed."),
            _ => throw new NotSupportedException(
                $"Unsupported ProjectFile.StorageDestination: {projectFile.StorageDestination}. No fallback is performed.")
        };
    }

    // ── FileServer ─────────────────────────────────────────────────────────
    private async Task<FileProjectFileResult> FileToFileServerAsync(
        SiNetSQLDbContext db,
        FileProjectFileRequest request,
        string conventionFileName,
        IReadOnlyList<string> folderPath,
        CancellationToken ct)
    {
        var root = await _fileServerRootResolver.ResolveAsync(db, request.ProjectId, ct);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException(
                $"FileServer root could not be resolved for project #{request.ProjectId}.");

        var destDir = root!;
        foreach (var seg in folderPath)
            destDir = Path.Combine(destDir, seg);
        Directory.CreateDirectory(destDir);

        var destFile = Path.Combine(destDir, conventionFileName);

        ArchiveResult? archiveResult = _versionArchiver.ArchiveIfExists(destFile);
        var currentVersionNumber = archiveResult?.NextActiveVersionNumber ?? 1;

        if (File.Exists(destFile))
        {
            try
            {
                var attrs = File.GetAttributes(destFile);
                if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    File.SetAttributes(destFile, attrs & ~FileAttributes.ReadOnly);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"[ProjectFileFiling] Could not clear ReadOnly on '{destFile}': {ex.Message}");
            }
        }

        File.Copy(request.SourceLocalPath, destFile, overwrite: true);

        var metadataTarget = !string.IsNullOrEmpty(request.FolderNameOverride)
            ? Path.Combine(destDir, request.FolderNameOverride!)
            : destFile;
        _metadataStore.Write(metadataTarget, new FilePlacementMetadata
        {
            OriginalFileName = request.OriginalFileName,
            ConventionFileName = !string.IsNullOrEmpty(request.FolderNameOverride) ? request.FolderNameOverride! : conventionFileName,
            CurrentVersionNumber = !string.IsNullOrEmpty(request.FolderNameOverride) ? 1 : currentVersionNumber,
            EmailSubject = request.EmailSubject,
            EmailFrom = request.EmailFrom,
            EmailDate = request.EmailDate,
            PlacedAtUtc = DateTime.UtcNow.ToString("o"),
        });

        return new FileProjectFileResult(
            PlacedFileName: conventionFileName,
            PlacedFilePath: destFile,
            StorageDestination: FileStorageDestination.FileServer,
            CurrentVersionNumber: currentVersionNumber,
            ArchivedPreviousVersion: archiveResult)
        {
            TargetDestination = FileStorageDestination.FileServer,
            TargetFileName = conventionFileName,
            TargetFilePath = destFile,
            TargetProjectId = request.ProjectId,
            TargetProjectFileId = request.ProjectFileId,
            TargetProjectAlternativeId = request.ProjectAlternativeId is > 0 ? request.ProjectAlternativeId : null,
        };
    }

    // ── ACC ────────────────────────────────────────────────────────────────
    private async Task<FileProjectFileResult> FileToAccAsync(
        SiNetSQLDbContext db,
        FileProjectFileRequest request,
        string conventionFileName,
        IReadOnlyList<string> folderPath,
        CancellationToken ct)
    {
        var mapping = await ResolveAccMappingAsync(db, request.ProjectId, ct).ConfigureAwait(false);

        if (_identityGuard is not null)
        {
            await _identityGuard
                .EnsureAllowedAsync(
                    IdentityOperationKind.AccFileWrite,
                    new IdentityOperationContext(
                        SiProjectId: request.ProjectId,
                        AccProjectId: mapping.AccProjectId),
                    ct)
                .ConfigureAwait(false);
        }

        var uploadRequest = new AccFileUploadRequest(
            mapping.AccProjectId!,
            request.SourceLocalPath,
            conventionFileName)
        {
            RootFolderId = mapping.AccTargetFolderId!,
            PathSegments = folderPath,
            SourceIdentity = BuildSourceIdentity(request),
            Snapshot = BuildSnapshot(request, conventionFileName),
            CompanionDocument = BuildCompanionDocument(request),
        };

        var uploadResult = await _accUploadService.UploadAsync(uploadRequest, ct);

        return new FileProjectFileResult(
            PlacedFileName: conventionFileName,
            PlacedFilePath: null,
            StorageDestination: FileStorageDestination.Acc,
            CurrentVersionNumber: 1,
            ArchivedPreviousVersion: null)
        {
            TargetDestination = FileStorageDestination.Acc,
            TargetFileName = conventionFileName,
            TargetAccItemId = uploadResult.ItemId,
            TargetAccVersionId = uploadResult.VersionId,
            TargetAccFolderId = uploadResult.FolderId,
            TargetProjectId = request.ProjectId,
            TargetProjectFileId = request.ProjectFileId,
            TargetProjectAlternativeId = request.ProjectAlternativeId is > 0 ? request.ProjectAlternativeId : null,
            AlreadySameSource = uploadResult.AlreadySameSource,
        };
    }

    /// <summary>
    /// Loads <see cref="ProjectAccMapping"/>; if missing/incomplete and a provisioner is wired,
    /// runs on-demand EnsureMapping then reloads. Fails closed with a clear error if still absent.
    /// </summary>
    private async Task<ProjectAccMapping> ResolveAccMappingAsync(
        SiNetSQLDbContext db,
        int projectId,
        CancellationToken ct)
    {
        var mapping = await db.ProjectAccMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId, ct)
            .ConfigureAwait(false);

        var needsProvision = mapping is null
            || string.IsNullOrEmpty(mapping.AccProjectId)
            || string.IsNullOrEmpty(mapping.AccTargetFolderId);

        if (needsProvision && _accMappingProvisioner is not null)
        {
            // #region agent log
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step(
                "Acc.Provision",
                $"on-demand EnsureMapping START project={projectId} reason={(mapping is null ? "missing" : "incomplete")}");
            // #endregion
            try
            {
                await _accMappingProvisioner.EnsureMappingAsync(projectId, ct).ConfigureAwait(false);
                // #region agent log
                WorkflowDebugTrace.Step("Acc.Provision", $"on-demand EnsureMapping OK project={projectId}");
                // #endregion
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    $"[ProjectFileFiling] EnsureMappingAsync failed for project #{projectId}: {ex.Message}");
                // #region agent log
                WorkflowDebugTrace.Step(
                    "Acc.Provision",
                    $"on-demand EnsureMapping FAILED project={projectId} {ex.GetType().Name}: {ex.Message}");
                // #endregion
                throw new InvalidOperationException(
                    $"חסר מיפוי ACC לפרויקט #{projectId}, וניסיון המיפוי האוטומטי נכשל: {ex.Message}",
                    ex);
            }

            // Reload after provision (same DbContext may have stale AsNoTracking cache — re-query).
            mapping = await db.ProjectAccMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ProjectId == projectId, ct)
                .ConfigureAwait(false);
        }

        if (mapping is null)
        {
            throw new InvalidOperationException(
                $"חסר מיפוי ACC לפרויקט #{projectId}. יש להשלים את מיפוי הפרויקט ב-ACC לפני תיוק.");
        }

        if (string.IsNullOrEmpty(mapping.AccProjectId) || string.IsNullOrEmpty(mapping.AccTargetFolderId))
        {
            throw new InvalidOperationException(
                $"מיפוי ACC לפרויקט #{projectId} חלקי (חסר AccProjectId/AccTargetFolderId).");
        }

        return mapping;
    }

    private static AccFileSourceIdentity? BuildSourceIdentity(FileProjectFileRequest request)
    {
        var sourceIdentity = new AccFileSourceIdentity(
            request.SourceGmailMessageId,
            request.SourceMessageDateUtc,
            request.SourceOriginalFileName,
            request.SourceFileSizeBytes,
            request.SourceContentSha256,
            request.SourceAttachmentId);

        return !string.IsNullOrWhiteSpace(sourceIdentity.GmailMessageId)
               || sourceIdentity.MessageDateUtc.HasValue
               || !string.IsNullOrWhiteSpace(sourceIdentity.OriginalFileName)
               || sourceIdentity.FileSizeBytes.HasValue
               || !string.IsNullOrWhiteSpace(sourceIdentity.ContentSha256)
               || sourceIdentity.AttachmentId is > 0
            ? sourceIdentity
            : null;
    }

    private static AccFileUploadSnapshot BuildSnapshot(
        FileProjectFileRequest request,
        string conventionFileName) =>
        new(
            LastFileName: conventionFileName,
            LastSizeBytes: request.SourceFileSizeBytes,
            LastSavedUtc: request.SourceMessageDateUtc,
            SourceFileNames: ResolveSourceFileNames(request),
            Notes: null,
            IsManualUpload: false,
            OriginalFolderPath: null);

    private static IReadOnlyList<string> ResolveSourceFileNames(FileProjectFileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceOriginalFileName))
            return new[] { request.SourceOriginalFileName! };

        if (!string.IsNullOrWhiteSpace(request.OriginalFileName))
            return new[] { request.OriginalFileName };

        return Array.Empty<string>();
    }

    private static AccFileUploadCompanionDocument? BuildCompanionDocument(FileProjectFileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FolderNameOverride))
            return null;

        var metadata = new FilePlacementMetadata
        {
            OriginalFileName = request.OriginalFileName,
            ConventionFileName = request.FolderNameOverride!,
            CurrentVersionNumber = 1,
            EmailSubject = request.EmailSubject,
            EmailFrom = request.EmailFrom,
            EmailDate = request.EmailDate,
            PlacedAtUtc = DateTime.UtcNow.ToString("o"),
        };

        var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return new AccFileUploadCompanionDocument(
            request.FolderNameOverride!.Trim() + ".json",
            json);
    }
}
