using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

internal static class AccFileTransferAttributeMap
{
    internal static class SnapshotNames
    {
        public const string LastFileName = "SiLastFileName";
        public const string LastSizeBytes = "SiLastSizeBytes";
        public const string LastSavedUtc = "SiLastSavedUtc";
        public const string SourceFileNames = "SiSourceFileNames";
        public const string Notes = "SiNotes";
        public const string ManualUpload = "SiManualUpload";
        public const string OriginalFolderPath = "SiOriginalFolderPath";
    }

    internal static class SourceNames
    {
        public const string GmailMessageId = "SiInbox.Source.GmailMessageId";
        public const string MessageDateUtc = "SiInbox.Source.MessageDateUtc";
        public const string OriginalFileName = "SiInbox.Source.OriginalFileName";
        public const string FileSizeBytes = "SiInbox.Source.FileSizeBytes";
        public const string ContentSha256 = "SiInbox.Source.ContentSha256";
        public const string AttachmentId = "SiInbox.Source.AttachmentId";
    }

    public static IReadOnlyDictionary<string, string?> ToSourceAttributes(AccFileSourceIdentity? sourceIdentity)
    {
        if (sourceIdentity is null)
        {
            return Empty;
        }

        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(sourceIdentity.GmailMessageId))
        {
            attributes[SourceNames.GmailMessageId] = sourceIdentity.GmailMessageId;
        }

        if (sourceIdentity.MessageDateUtc.HasValue)
        {
            attributes[SourceNames.MessageDateUtc] = sourceIdentity.MessageDateUtc.Value
                .ToUniversalTime()
                .ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(sourceIdentity.OriginalFileName))
        {
            attributes[SourceNames.OriginalFileName] = sourceIdentity.OriginalFileName;
        }

        if (sourceIdentity.FileSizeBytes.HasValue)
        {
            attributes[SourceNames.FileSizeBytes] = sourceIdentity.FileSizeBytes.Value
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(sourceIdentity.ContentSha256))
        {
            attributes[SourceNames.ContentSha256] = sourceIdentity.ContentSha256;
        }

        if (sourceIdentity.AttachmentId is > 0)
        {
            attributes[SourceNames.AttachmentId] = sourceIdentity.AttachmentId.Value
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return attributes;
    }

    public static IReadOnlyDictionary<string, string?> ToSnapshotAttributes(
        AccFileUploadSnapshot? snapshot,
        string localSourcePath,
        string displayName)
    {
        if (snapshot is null)
        {
            return Empty;
        }

        var sourceFileInfo = TryGetFileInfo(localSourcePath);
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
        var lastFileName = string.IsNullOrWhiteSpace(snapshot.LastFileName) ? displayName : snapshot.LastFileName;
        if (!string.IsNullOrWhiteSpace(lastFileName))
        {
            attributes[SnapshotNames.LastFileName] = lastFileName;
        }

        var lastSizeBytes = snapshot.LastSizeBytes ?? sourceFileInfo?.Length;
        if (lastSizeBytes.HasValue)
        {
            attributes[SnapshotNames.LastSizeBytes] = lastSizeBytes.Value
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var lastSavedUtc = snapshot.LastSavedUtc ?? sourceFileInfo?.LastWriteTimeUtc;
        if (lastSavedUtc.HasValue)
        {
            attributes[SnapshotNames.LastSavedUtc] = lastSavedUtc.Value
                .ToUniversalTime()
                .ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        var sourceFileNames = (snapshot.SourceFileNames ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceFileNames.Length > 0)
        {
            attributes[SnapshotNames.SourceFileNames] = string.Join('\n', sourceFileNames);
        }
        else if (!string.IsNullOrWhiteSpace(lastFileName))
        {
            attributes[SnapshotNames.SourceFileNames] = lastFileName;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Notes))
        {
            attributes[SnapshotNames.Notes] = snapshot.Notes;
        }

        if (snapshot.IsManualUpload)
        {
            attributes[SnapshotNames.ManualUpload] = "1";
        }

        if (!string.IsNullOrWhiteSpace(snapshot.OriginalFolderPath))
        {
            attributes[SnapshotNames.OriginalFolderPath] = snapshot.OriginalFolderPath;
        }

        return attributes;
    }

    public static bool HasSourceIdentity(AccFileSourceIdentity? sourceIdentity) =>
        sourceIdentity is not null
        && (!string.IsNullOrWhiteSpace(sourceIdentity.GmailMessageId)
            || sourceIdentity.MessageDateUtc.HasValue
            || !string.IsNullOrWhiteSpace(sourceIdentity.OriginalFileName)
            || sourceIdentity.FileSizeBytes.HasValue
            || !string.IsNullOrWhiteSpace(sourceIdentity.ContentSha256)
            || sourceIdentity.AttachmentId is > 0);

    private static FileInfo? TryGetFileInfo(string localSourcePath)
    {
        try
        {
            var fileInfo = new FileInfo(localSourcePath);
            return fileInfo.Exists ? fileInfo : null;
        }
        catch
        {
            return null;
        }
    }

    private static readonly IReadOnlyDictionary<string, string?> Empty =
        new Dictionary<string, string?>(StringComparer.Ordinal);
}
