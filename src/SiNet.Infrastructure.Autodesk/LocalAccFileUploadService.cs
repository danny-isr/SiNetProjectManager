using MyOffice.AutodeskConnector;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class LocalAccFileUploadService(IAccTransferConnector connector) : IAccFileUploadService
{
    private readonly IAccTransferConnector _connector = connector;

    public async Task<AccFileUploadResult> UploadAsync(
        AccFileUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var targetFolderId = await ResolveTargetFolderIdAsync(request, cancellationToken).ConfigureAwait(false);
        var displayName = request.DisplayName.Trim();
        var duplicate = await ResolveDuplicateAsync(request, targetFolderId, displayName, cancellationToken).ConfigureAwait(false);

        if (duplicate is not null
            && AccFileTransferAttributeMap.HasSourceIdentity(request.SourceIdentity)
            && await IsSameSourceAsync(request.ProjectId, duplicate.ItemId, request.SourceIdentity!, cancellationToken).ConfigureAwait(false))
        {
            return new AccFileUploadResult(
                targetFolderId,
                duplicate.ItemId,
                VersionId: null,
                displayName,
                AlreadySameSource: true);
        }

        var stagedPath = StageWithDisplayName(request.LocalSourcePath, displayName);
        try
        {
            var uploadResult = duplicate is null
                ? await _connector.UploadFileFinalAsync(
                    request.ProjectId,
                    targetFolderId,
                    stagedPath,
                    displayName,
                    cancellationToken).ConfigureAwait(false)
                : await _connector.UploadNewVersionAsync(
                    request.ProjectId,
                    targetFolderId,
                    duplicate.ItemId,
                    stagedPath,
                    cancellationToken).ConfigureAwait(false);

            await TryWriteAttributesAsync(
                request.ProjectId,
                targetFolderId,
                uploadResult.ItemId,
                uploadResult.VersionId,
                AccFileTransferAttributeMap.ToSourceAttributes(request.SourceIdentity),
                cancellationToken).ConfigureAwait(false);

            await TryWriteAttributesAsync(
                request.ProjectId,
                targetFolderId,
                uploadResult.ItemId,
                uploadResult.VersionId,
                AccFileTransferAttributeMap.ToSnapshotAttributes(request.Snapshot, request.LocalSourcePath, displayName),
                cancellationToken).ConfigureAwait(false);

            await TryUploadCompanionDocumentAsync(
                request.ProjectId,
                targetFolderId,
                request.CompanionDocument,
                cancellationToken).ConfigureAwait(false);

            return new AccFileUploadResult(
                targetFolderId,
                uploadResult.ItemId,
                uploadResult.VersionId,
                displayName,
                AlreadySameSource: false);
        }
        finally
        {
            TryDeleteStagedFile(stagedPath, request.LocalSourcePath);
        }
    }

    private async Task<string> ResolveTargetFolderIdAsync(
        AccFileUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetFolderId))
        {
            return request.TargetFolderId.Trim();
        }

        var rootFolderId = request.RootFolderId?.Trim();
        if (string.IsNullOrWhiteSpace(rootFolderId))
        {
            throw new InvalidOperationException("ACC upload requires either TargetFolderId or RootFolderId.");
        }

        var pathSegments = request.PathSegments
            .Where(static segment => !string.IsNullOrWhiteSpace(segment))
            .Select(static segment => segment.Trim())
            .ToArray();
        if (pathSegments.Length == 0)
        {
            return rootFolderId;
        }

        return await _connector
            .EnsureFolderPathAsync(request.ProjectId, rootFolderId, pathSegments, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AccFolderItem?> ResolveDuplicateAsync(
        AccFileUploadRequest request,
        string targetFolderId,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ExistingItemId))
        {
            return new AccFolderItem
            {
                ItemId = request.ExistingItemId.Trim(),
                DisplayName = displayName,
            };
        }

        var items = await _connector
            .GetFolderItemsAsync(request.ProjectId, targetFolderId, cancellationToken)
            .ConfigureAwait(false);

        return items.FirstOrDefault(item =>
            string.Equals(item.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> IsSameSourceAsync(
        string projectId,
        string itemId,
        AccFileSourceIdentity sourceIdentity,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _connector
                .GetItemCustomAttributesAsync(projectId, itemId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success || result.Value is null)
            {
                return false;
            }

            var attributes = result.Value;
            if (!string.IsNullOrWhiteSpace(sourceIdentity.ContentSha256)
                && attributes.TryGetValue(AccFileTransferAttributeMap.SourceNames.ContentSha256, out var existingSha)
                && !string.IsNullOrWhiteSpace(existingSha))
            {
                return string.Equals(existingSha, sourceIdentity.ContentSha256, StringComparison.OrdinalIgnoreCase);
            }

            var gmailMatch = MatchString(attributes, AccFileTransferAttributeMap.SourceNames.GmailMessageId, sourceIdentity.GmailMessageId);
            var attachmentMatch = MatchInt(attributes, AccFileTransferAttributeMap.SourceNames.AttachmentId, sourceIdentity.AttachmentId);
            var hasStrongIdentifier = gmailMatch == MatchResult.Equal || attachmentMatch == MatchResult.Equal;
            if (!hasStrongIdentifier)
            {
                return false;
            }

            var messageDateMatch = MatchDate(attributes, AccFileTransferAttributeMap.SourceNames.MessageDateUtc, sourceIdentity.MessageDateUtc);
            var sizeMatch = MatchLong(attributes, AccFileTransferAttributeMap.SourceNames.FileSizeBytes, sourceIdentity.FileSizeBytes);
            var nameMatch = MatchString(attributes, AccFileTransferAttributeMap.SourceNames.OriginalFileName, sourceIdentity.OriginalFileName);

            return messageDateMatch is not MatchResult.NotEqual and not MatchResult.Missing
                && sizeMatch is not MatchResult.NotEqual and not MatchResult.Missing
                && nameMatch is not MatchResult.NotEqual and not MatchResult.Missing;
        }
        catch
        {
            return false;
        }
    }

    private async Task TryWriteAttributesAsync(
        string projectId,
        string folderId,
        string itemId,
        string? versionId,
        IReadOnlyDictionary<string, string?> attributes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(versionId) || attributes.Count == 0)
        {
            return;
        }

        try
        {
            await _connector
                .SetItemCustomAttributesAsync(projectId, folderId, versionId, attributes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Best-effort by design: metadata failures must not turn a completed upload into failure.
        }
    }

    private async Task TryUploadCompanionDocumentAsync(
        string projectId,
        string folderId,
        AccFileUploadCompanionDocument? companionDocument,
        CancellationToken cancellationToken)
    {
        if (companionDocument is null
            || string.IsNullOrWhiteSpace(companionDocument.FileName)
            || companionDocument.ContentText is null)
        {
            return;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "SiNetAccCompanion", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, companionDocument.FileName.Trim());
        await File.WriteAllTextAsync(tempPath, companionDocument.ContentText, System.Text.Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        var stagedPath = StageWithDisplayName(tempPath, companionDocument.FileName.Trim());
        try
        {
            var duplicate = (await _connector
                    .GetFolderItemsAsync(projectId, folderId, cancellationToken)
                    .ConfigureAwait(false))
                .FirstOrDefault(item =>
                    string.Equals(item.DisplayName, companionDocument.FileName, StringComparison.OrdinalIgnoreCase));

            if (duplicate is null)
            {
                await _connector
                    .UploadFileFinalAsync(projectId, folderId, stagedPath, companionDocument.FileName, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _connector
                    .UploadNewVersionAsync(projectId, folderId, duplicate.ItemId, stagedPath, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort by design: companion JSON drift must not turn the primary upload into failure.
        }
        finally
        {
            TryDeleteStagedFile(stagedPath, tempPath);
            TryDeleteStagedFile(tempPath, string.Empty);
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static void ValidateRequest(AccFileUploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new ArgumentException("ProjectId is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.LocalSourcePath))
        {
            throw new ArgumentException("LocalSourcePath is required.", nameof(request));
        }

        if (!File.Exists(request.LocalSourcePath))
        {
            throw new FileNotFoundException("ACC upload source file not found.", request.LocalSourcePath);
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new ArgumentException("DisplayName is required.", nameof(request));
        }
    }

    private static string StageWithDisplayName(string sourcePath, string displayName)
    {
        if (string.Equals(Path.GetFileName(sourcePath), displayName, StringComparison.Ordinal))
        {
            return sourcePath;
        }

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "SiNetAccUpload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var stagedPath = Path.Combine(stagingDirectory, displayName);
        File.Copy(sourcePath, stagedPath, overwrite: true);
        return stagedPath;
    }

    private static void TryDeleteStagedFile(string stagedPath, string originalSourcePath)
    {
        if (string.Equals(stagedPath, originalSourcePath, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (File.Exists(stagedPath))
            {
                File.Delete(stagedPath);
            }

            var directory = Path.GetDirectoryName(stagedPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static MatchResult MatchString(IReadOnlyDictionary<string, string?> attributes, string key, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return MatchResult.Missing;
        }

        if (!attributes.TryGetValue(key, out var actual) || string.IsNullOrWhiteSpace(actual))
        {
            return MatchResult.Missing;
        }

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            ? MatchResult.Equal
            : MatchResult.NotEqual;
    }

    private static MatchResult MatchLong(IReadOnlyDictionary<string, string?> attributes, string key, long? expected)
    {
        if (!expected.HasValue)
        {
            return MatchResult.Missing;
        }

        if (!attributes.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return MatchResult.Missing;
        }

        return long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
               && value == expected.Value
            ? MatchResult.Equal
            : MatchResult.NotEqual;
    }

    private static MatchResult MatchInt(IReadOnlyDictionary<string, string?> attributes, string key, int? expected)
    {
        if (expected is not > 0)
        {
            return MatchResult.Missing;
        }

        if (!attributes.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return MatchResult.Missing;
        }

        return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
               && value == expected.Value
            ? MatchResult.Equal
            : MatchResult.NotEqual;
    }

    private static MatchResult MatchDate(IReadOnlyDictionary<string, string?> attributes, string key, DateTime? expected)
    {
        if (!expected.HasValue)
        {
            return MatchResult.Missing;
        }

        if (!attributes.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return MatchResult.Missing;
        }

        if (!DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var actual))
        {
            return MatchResult.NotEqual;
        }

        return actual.ToUniversalTime() == expected.Value.ToUniversalTime()
            ? MatchResult.Equal
            : MatchResult.NotEqual;
    }

    private enum MatchResult
    {
        Missing,
        Equal,
        NotEqual,
    }
}
