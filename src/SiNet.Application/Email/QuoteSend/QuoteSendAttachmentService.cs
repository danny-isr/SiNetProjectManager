using System.IO;
using SiNet.Application.Diagnostics;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;

namespace SiNet.Application.Email.QuoteSend;

/// <summary>
/// Files a SendQuote PDF into catalog <see cref="IQuoteSendAttachmentService.CatalogCode"/>
/// via FileServer when the slot is empty; otherwise leaves the existing filing alone.
/// </summary>
public sealed class QuoteSendAttachmentService : IQuoteSendAttachmentService
{
    private readonly IProjectFileQueryService _fileQuery;
    private readonly IFileIndexService _fileIndex;

    public QuoteSendAttachmentService(
        IProjectFileQueryService fileQuery,
        IFileIndexService fileIndex)
    {
        _fileQuery = fileQuery ?? throw new ArgumentNullException(nameof(fileQuery));
        _fileIndex = fileIndex ?? throw new ArgumentNullException(nameof(fileIndex));
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAttachInitialDirectoryAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
            return null;

        var resolved = await ResolveSlotAsync(projectId, cancellationToken).ConfigureAwait(false);
        return resolved?.FolderPath;
    }

    /// <inheritdoc />
    public async Task<QuoteSendEnsureFiledResult> EnsureFiledIfNeededAsync(
        int projectId,
        string sourcePdfPath,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
            return Fail(sourcePdfPath, "Invalid project id.");
        if (string.IsNullOrWhiteSpace(sourcePdfPath))
            return Fail(sourcePdfPath ?? string.Empty, "Source path is required.");
        if (!File.Exists(sourcePdfPath))
            return Fail(sourcePdfPath, "Source PDF was not found.");

        var ext = Path.GetExtension(sourcePdfPath);
        if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            return Fail(sourcePdfPath, "Only PDF files can be filed as QuoteSendDocument.");

        var slot = await ResolveSlotAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (slot is null)
            return Fail(sourcePdfPath, "Catalog slot QuoteSendDocument was not found for this project.");
        if (string.IsNullOrWhiteSpace(slot.FolderPath))
            return Fail(sourcePdfPath, "Could not resolve FileServer path for ניהול_כספי.");
        if (slot.Definition.ProjectType is not int projectType || slot.Definition.Number is not int fileNumber)
            return Fail(sourcePdfPath, "QuoteSendDocument has no TypeProjId/Number identity.");
        if (slot.ProjectNumber <= 0)
            return Fail(sourcePdfPath, "Project number is missing; cannot build a canonical file name.");

        var store = _fileIndex.GetStore(FileStorageDestination.FileServer);
        if (store is null)
            return Fail(sourcePdfPath, "FileServer store is not registered.");

        ScannedFile? existingMatch = null;
        await foreach (var sf in store.ListFilesAsync(slot.FolderPath, cancellationToken).ConfigureAwait(false))
        {
            if (sf.Parsed is { } parsed
                && parsed.ProjectType == projectType
                && parsed.Number == fileNumber)
            {
                existingMatch = sf;
                break;
            }
        }

        if (existingMatch is not null)
        {
            WorkflowDebugTrace.Step(
                "QuoteSend.File",
                $"project={projectId} alreadyFiled=True file={existingMatch.FileName}");
            return new QuoteSendEnsureFiledResult(
                Success: true,
                AlreadyFiled: true,
                FiledNow: false,
                SourcePath: sourcePdfPath,
                FiledCanonicalPath: existingMatch.NativeId,
                Error: null);
        }

        var targetName = ProjectFileNameBuilder.Build(
            slot.ProjectNumber,
            projectType,
            fileNumber,
            alternative: "1",
            version: 1,
            projectFileTitle: slot.Definition.BaseName,
            originalFileName: Path.GetFileName(sourcePdfPath));

        try
        {
            _fileIndex.MarkInFlight(projectId, targetName, FileStorageDestination.FileServer);
            var placed = await store
                .UploadAsync(slot.FolderPath, sourcePdfPath, targetName, cancellationToken)
                .ConfigureAwait(false);
            WorkflowDebugTrace.Step(
                "QuoteSend.File",
                $"project={projectId} filed=True file={placed.FileName}");
            return new QuoteSendEnsureFiledResult(
                Success: true,
                AlreadyFiled: false,
                FiledNow: true,
                SourcePath: sourcePdfPath,
                FiledCanonicalPath: placed.NativeId,
                Error: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            WorkflowDebugTrace.Step(
                "QuoteSend.File",
                $"project={projectId} filed=False error={ex.Message}");
            return Fail(sourcePdfPath, ex.Message);
        }
        finally
        {
            _fileIndex.ClearInFlight(projectId, targetName, FileStorageDestination.FileServer);
        }
    }

    private async Task<ResolvedSlot?> ResolveSlotAsync(int projectId, CancellationToken cancellationToken)
    {
        var tree = await _fileQuery.GetProjectFileTreeAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (tree is null)
            return null;

        var def = FindByCode(tree.RootFolders, IQuoteSendAttachmentService.CatalogCode);
        if (def is null || def.FolderId <= 0)
            return null;

        var store = _fileIndex.GetStore(FileStorageDestination.FileServer);
        if (store is null)
            return new ResolvedSlot(tree.ProjectNumber, def, FolderPath: null);

        var folderPath = await store
            .ResolveFolderHandleAsync(projectId, def.FolderId, cancellationToken)
            .ConfigureAwait(false);

        return new ResolvedSlot(tree.ProjectNumber, def, folderPath);
    }

    private static ProjectFileDefinitionDto? FindByCode(
        IReadOnlyList<ProjectFolderDto> folders,
        string code)
    {
        foreach (var folder in folders)
        {
            foreach (var file in folder.Files)
            {
                if (string.Equals(file.Code, code, StringComparison.Ordinal))
                    return file;
            }

            var nested = FindByCode(folder.Children, code);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static QuoteSendEnsureFiledResult Fail(string sourcePath, string error) =>
        new(
            Success: false,
            AlreadyFiled: false,
            FiledNow: false,
            SourcePath: sourcePath,
            FiledCanonicalPath: null,
            Error: error);

    private sealed record ResolvedSlot(
        int ProjectNumber,
        ProjectFileDefinitionDto Definition,
        string? FolderPath);
}
