using System.IO;
using SiNet.Application.Diagnostics;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;

namespace SiNet.Application.Email.QuoteSend;

/// <summary>
/// Copies a SendQuote PDF into catalog <see cref="IQuoteSendAttachmentService.CatalogCode"/>
/// via FileServer (original path unchanged). Skips copy only when the selected file is already
/// that catalog identity.
/// </summary>
public sealed class QuoteSendAttachmentService : IQuoteSendAttachmentService
{
    private static readonly string[] FinanceFolderTitles =
    [
        "\u05E0\u05D9\u05D4\u05D5\u05DC_\u05DB\u05E1\u05E4\u05D9", // ניהול_כספי
        "\u05E0\u05D9\u05D4\u05D5\u05DC \u05DB\u05E1\u05E4\u05D9", // ניהול כספי
    ];

    private static readonly string[] FinanceFolderFallbackCodes =
    [
        IQuoteSendAttachmentService.CatalogCode,
        "QuoteDocument",
        "QuoteEstimate",
        "QuoteClientApproval",
    ];

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

        var tree = await _fileQuery.GetProjectFileTreeAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (tree is null)
        {
            WorkflowDebugTrace.Step("QuoteSend.File", $"project={projectId} initialDir=null reason=no-tree");
            return null;
        }

        var folderId = ResolveFinanceFolderId(tree.RootFolders);
        if (folderId is null or <= 0)
        {
            WorkflowDebugTrace.Step("QuoteSend.File", $"project={projectId} initialDir=null reason=no-finance-folder");
            return null;
        }

        var store = _fileIndex.GetStore(FileStorageDestination.FileServer);
        if (store is null)
        {
            WorkflowDebugTrace.Step("QuoteSend.File", $"project={projectId} initialDir=null reason=no-fileserver-store");
            return null;
        }

        var folderPath = await store
            .ResolveFolderHandleAsync(projectId, folderId.Value, cancellationToken)
            .ConfigureAwait(false);
        var existing = FirstExistingDirectory(folderPath);
        WorkflowDebugTrace.Step(
            "QuoteSend.File",
            $"project={projectId} initialDir={(existing ?? "(missing)")} folderId={folderId} resolved={folderPath ?? "(null)"}");
        return existing;
    }

    /// <inheritdoc />
    public async Task<QuoteSendEnsureFiledResult> EnsureFiledIfNeededAsync(
        int projectId,
        string sourcePdfPath,
        string? alternativeName = null,
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
            return Fail(sourcePdfPath, "Catalog slot QuoteSendDocument was not found for this project. Run basic seed.");
        if (string.IsNullOrWhiteSpace(slot.FolderPath))
            return Fail(sourcePdfPath, "Could not resolve FileServer path for ניהול_כספי.");
        if (slot.Definition.ProjectType is not int projectType || slot.Definition.Number is not int fileNumber)
            return Fail(sourcePdfPath, "QuoteSendDocument has no TypeProjId/Number identity.");
        if (slot.ProjectNumber <= 0)
            return Fail(sourcePdfPath, "Project number is missing; cannot build a canonical file name.");

        var store = _fileIndex.GetStore(FileStorageDestination.FileServer);
        if (store is null)
            return Fail(sourcePdfPath, "FileServer store is not registered.");

        var folderPath = FirstExistingDirectory(slot.FolderPath) ?? slot.FolderPath;
        var sourceFullPath = Path.GetFullPath(sourcePdfPath);

        // Skip copy only when the selected file itself is already this catalog identity.
        var sourceParsed = ProjectFileNameParser.TryParse(Path.GetFileName(sourcePdfPath));
        if (sourceParsed is { } sp
            && sp.ProjectType == projectType
            && sp.Number == fileNumber
            && sp.ProjectNumber == slot.ProjectNumber)
        {
            WorkflowDebugTrace.Step(
                "QuoteSend.File",
                $"project={projectId} alreadyFiled=True selectedIsSendDoc file={Path.GetFileName(sourcePdfPath)}");
            return OkAlreadyFiled(sourcePdfPath, sourceFullPath);
        }

        var existingAlts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var sf in store.ListFilesAsync(folderPath, cancellationToken).ConfigureAwait(false))
        {
            if (sf.Parsed is not { } parsed
                || parsed.ProjectType != projectType
                || parsed.Number != fileNumber)
                continue;

            if (string.Equals(
                    Path.GetFullPath(sf.NativeId),
                    sourceFullPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                WorkflowDebugTrace.Step(
                    "QuoteSend.File",
                    $"project={projectId} alreadyFiled=True samePath file={sf.FileName}");
                return OkAlreadyFiled(sourcePdfPath, sf.NativeId);
            }

            var alt = string.IsNullOrWhiteSpace(parsed.Alternative) ? "1" : parsed.Alternative;
            existingAlts.Add(alt);
        }

        var existingList = existingAlts.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
        var suggested = SuggestNextAlternative(existingList);

        if (existingList.Count > 0 && string.IsNullOrWhiteSpace(alternativeName))
        {
            WorkflowDebugTrace.Step(
                "QuoteSend.File",
                $"project={projectId} requiresNewAlternative existing=[{string.Join(',', existingList)}] suggested={suggested}");
            return new QuoteSendEnsureFiledResult(
                Success: false,
                AlreadyFiled: false,
                FiledNow: false,
                RequiresNewAlternative: true,
                ExistingAlternatives: existingList,
                SuggestedAlternative: suggested,
                SourcePath: sourcePdfPath,
                FiledCanonicalPath: null,
                Error: "כבר קיימת הצעת מחיר לשליחה. יש לבחור אלטרנטיבה אחרת לקובץ החדש, או לבחור את הקובץ המתויק הקיים.");
        }

        var altToUse = string.IsNullOrWhiteSpace(alternativeName)
            ? "1"
            : alternativeName.Trim();

        if (existingAlts.Contains(altToUse))
        {
            return Fail(
                sourcePdfPath,
                $"אלטרנטיבה '{altToUse}' כבר קיימת להצעת מחיר לשליחה. בחר שם אלטרנטיבה אחר.");
        }

        var targetName = ProjectFileNameBuilder.Build(
            slot.ProjectNumber,
            projectType,
            fileNumber,
            alternative: altToUse,
            version: 1,
            projectFileTitle: slot.Definition.BaseName,
            originalFileName: Path.GetFileName(sourcePdfPath));

        try
        {
            _fileIndex.MarkInFlight(projectId, targetName, FileStorageDestination.FileServer);
            // UploadAsync copies; source path is left unchanged.
            var placed = await store
                .UploadAsync(folderPath, sourcePdfPath, targetName, cancellationToken)
                .ConfigureAwait(false);
            WorkflowDebugTrace.Step(
                "QuoteSend.File",
                $"project={projectId} filed=True copy=True file={placed.FileName} alt={altToUse}");
            return new QuoteSendEnsureFiledResult(
                Success: true,
                AlreadyFiled: false,
                FiledNow: true,
                RequiresNewAlternative: false,
                ExistingAlternatives: existingList,
                SuggestedAlternative: null,
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

    private static int? ResolveFinanceFolderId(IReadOnlyList<ProjectFolderDto> roots)
    {
        foreach (var code in FinanceFolderFallbackCodes)
        {
            var def = FindByCode(roots, code);
            if (def is { FolderId: > 0 })
                return def.FolderId;
        }

        return FindFolderIdByTitles(roots, FinanceFolderTitles);
    }

    private static int? FindFolderIdByTitles(IReadOnlyList<ProjectFolderDto> folders, IReadOnlyList<string> titles)
    {
        foreach (var folder in folders)
        {
            if (titles.Any(t => string.Equals(folder.Name, t, StringComparison.Ordinal)))
                return folder.FolderId;

            var nested = FindFolderIdByTitles(folder.Children, titles);
            if (nested is not null)
                return nested;
        }

        return null;
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

    /// <summary>
    /// Returns <paramref name="path"/> when it exists; otherwise tries underscore↔space aliases
    /// on each segment (legacy FileServer folders may still use spaces).
    /// </summary>
    public static string? FirstExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (Directory.Exists(path))
            return path;

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path };
        var swapped = path.Replace('_', '\u0001').Replace(' ', '_').Replace('\u0001', ' ');
        candidates.Add(swapped);

        // Also try swapping only the leaf segment.
        try
        {
            var parent = Path.GetDirectoryName(path);
            var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
            {
                var altLeaf = leaf.Contains('_', StringComparison.Ordinal)
                    ? leaf.Replace('_', ' ')
                    : leaf.Replace(' ', '_');
                candidates.Add(Path.Combine(parent, altLeaf));
            }
        }
        catch (ArgumentException)
        {
            // ignore malformed paths
        }

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string SuggestNextAlternative(IReadOnlyList<string> existing)
    {
        var n = 2;
        while (existing.Any(a => string.Equals(a, n.ToString(), StringComparison.OrdinalIgnoreCase)))
            n++;
        return n.ToString();
    }

    private static QuoteSendEnsureFiledResult OkAlreadyFiled(string sourcePath, string filedPath) =>
        new(
            Success: true,
            AlreadyFiled: true,
            FiledNow: false,
            RequiresNewAlternative: false,
            ExistingAlternatives: Array.Empty<string>(),
            SuggestedAlternative: null,
            SourcePath: sourcePath,
            FiledCanonicalPath: filedPath,
            Error: null);

    private static QuoteSendEnsureFiledResult Fail(string sourcePath, string error) =>
        new(
            Success: false,
            AlreadyFiled: false,
            FiledNow: false,
            RequiresNewAlternative: false,
            ExistingAlternatives: Array.Empty<string>(),
            SuggestedAlternative: null,
            SourcePath: sourcePath,
            FiledCanonicalPath: null,
            Error: error);

    private sealed record ResolvedSlot(
        int ProjectNumber,
        ProjectFileDefinitionDto Definition,
        string? FolderPath);
}
