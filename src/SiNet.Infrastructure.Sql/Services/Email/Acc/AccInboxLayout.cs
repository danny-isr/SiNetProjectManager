namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>Role of a file inside the ACC Inbox message/attachments layout.</summary>
public enum AccInboxFileRole
{
    EmailBodyPdf,
    Manifest,
    Attachment,
    ExternalDownload,
    ZipExtractedAttachment,
    Unknown
}

/// <summary>
/// Single source of truth for ACC Inbox folder layout. Native port of the legacy
/// <c>SiNetSQL.Services.EmailIngestion.AccInboxLayout</c>.
/// <para>
/// Layout is <c>/_Inbox/THREAD_&lt;ThreadKey&gt;/MSG_&lt;MessageKey&gt;/</c> with
/// <c>00_Email.pdf</c>, <c>manifest.json</c>, and an <c>Attachments/</c> subfolder
/// underneath the MSG folder. Thread is the primary business grouping; date is
/// metadata on the message, NOT a folder level.
/// </para>
/// </summary>
public static class AccInboxLayout
{
    public const string MessageFolderPrefix = "MSG_";
    public const string ThreadFolderPrefix = "THREAD_";
    public const string AttachmentsFolderName = "Attachments";
    public const string EmailBodyFileName = "00_Email.pdf";
    public const string ManifestFileName = "manifest.json";

    /// <summary>
    /// Sentinel <c>EmailInboxAttachment.AttachmentIndex</c> for the body PDF row (Legacy parity).
    /// </summary>
    public const int EmailBodyAttachmentIndex = -11;

    /// <summary>Returns the MSG folder name (e.g. <c>MSG_ab12cd34</c>).</summary>
    public static string GetMessageFolderName(string messageKey) => MessageFolderPrefix + messageKey;

    /// <summary>Returns the THREAD folder name (e.g. <c>THREAD_ef56gh78</c>).</summary>
    public static string GetThreadFolderName(string threadKey)
    {
        if (string.IsNullOrWhiteSpace(threadKey))
            throw new ArgumentException("ThreadKey is required to build the ACC Inbox thread folder name.", nameof(threadKey));
        return ThreadFolderPrefix + threadKey;
    }

    /// <summary>
    /// Returns the ordered folder name components, relative to the Inbox root folder,
    /// that lead to the message folder:
    /// <c>[ "THREAD_&lt;ThreadKey&gt;", "MSG_&lt;MessageKey&gt;" ]</c>.
    /// </summary>
    public static IReadOnlyList<string> BuildMessageFolderPath(string threadKey, string messageKey)
    {
        if (string.IsNullOrWhiteSpace(messageKey))
            throw new ArgumentException("MessageKey is required to build the ACC Inbox message folder path.", nameof(messageKey));
        return new[] { GetThreadFolderName(threadKey), GetMessageFolderName(messageKey) };
    }

    /// <summary>Detection-only helper: legacy <c>MSG_</c> folder name.</summary>
    public static bool IsLegacyMessageFolderName(string? folderName) =>
        !string.IsNullOrEmpty(folderName)
        && folderName.StartsWith(MessageFolderPrefix, StringComparison.Ordinal);

    /// <summary>Detection-only helper: current-layout <c>THREAD_</c> folder name.</summary>
    public static bool IsThreadFolderName(string? folderName) =>
        !string.IsNullOrEmpty(folderName)
        && folderName.StartsWith(ThreadFolderPrefix, StringComparison.Ordinal);

    public static AccInboxFileRole GetRole(
        int attachmentIndex,
        string? fileName,
        bool isExternalDownload,
        string? subfolderName = null)
    {
        if (attachmentIndex == EmailBodyAttachmentIndex || IsEmailBodyFile(fileName))
            return AccInboxFileRole.EmailBodyPdf;
        if (IsManifestFile(fileName))
            return AccInboxFileRole.Manifest;
        if (!string.IsNullOrWhiteSpace(subfolderName))
            return AccInboxFileRole.ZipExtractedAttachment;
        return isExternalDownload
            ? AccInboxFileRole.ExternalDownload
            : AccInboxFileRole.Attachment;
    }

    public static bool IsEmailBodyFile(string? fileName) =>
        string.Equals(fileName, EmailBodyFileName, StringComparison.OrdinalIgnoreCase);

    public static bool IsManifestFile(string? fileName) =>
        string.Equals(fileName, ManifestFileName, StringComparison.OrdinalIgnoreCase);

    public static bool UsesMessageFolder(AccInboxFileRole role) =>
        role is AccInboxFileRole.EmailBodyPdf or AccInboxFileRole.Manifest;

    public static bool UsesAttachmentsFolder(AccInboxFileRole role) =>
        role is AccInboxFileRole.Attachment or AccInboxFileRole.ExternalDownload or AccInboxFileRole.ZipExtractedAttachment;
}
