namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>
/// Well-known ACC Custom Attribute names used when persisting SiNet file/inbox
/// metadata onto an ACC item. Native port of the constant catalogs from the
/// legacy <c>SiNetSQL.FileIndex.SidecarMetadata</c>.
/// <para>
/// Only the attribute-name constants are ported here — the ACC read/write of the
/// values is owned by <see cref="SiNet.Application.Abstractions.Autodesk.IAccItemMetadataService"/>,
/// and the file-server sidecar JSON is owned by <c>FileServerMetadataStore</c>.
/// NOTE: all attribute names MUST be ≤32 chars (ACC API limit).
/// </para>
/// </summary>
public static class SidecarMetadata
{
    /// <summary>Well-known Custom Attribute names for a project file item.</summary>
    public static class AccAttributeNames
    {
        public const string LastFileName        = "SiLastFileName";
        public const string LastSizeBytes       = "SiLastSizeBytes";
        public const string LastSavedUtc        = "SiLastSavedUtc";
        public const string SourceFileNames     = "SiSourceFileNames"; // newline-separated
        public const string Notes               = "SiNotes";
        public const string ManualUpload        = "SiManualUpload";     // "1" when manually uploaded from an unmapped folder
        public const string OriginalFolderPath  = "SiOriginalFolderPath"; // source folder full path at upload time
    }

    /// <summary>
    /// Well-known ACC Custom Attribute names for Office Inbox files (tag / move / lock
    /// / source-identity / message-identity). These names are used only for ACC item
    /// metadata; the database may cache related values but must not be treated as the
    /// source of truth for Inbox tag/move/lock state.
    /// </summary>
    public static class InboxAccAttributeNames
    {
        public const string TagProjectFileId = "SiInbox.Tag.ProjectFileId";
        public const string TagProjectAlternativeId = "SiInbox.Tag.ProjectAlternativeId";
        public const string TagTaggedBy = "SiInbox.Tag.TaggedBy";
        public const string TagTaggedAtUtc = "SiInbox.Tag.TaggedAtUtc";
        public const string TagStatus = "SiInbox.Tag.Status";

        public const string MoveMovedToProject = "SiInbox.Move.MovedToProject";
        public const string MoveMovedAtUtc = "SiInbox.Move.MovedAtUtc";
        public const string MoveMovedBy = "SiInbox.Move.MovedBy";
        public const string MoveTargetDestination = "SiInbox.Move.TargetDestination";
        public const string MoveTargetProjectId = "SiInbox.Move.TargetProjectId";
        public const string MoveTargetProjectFileId = "SiInbox.Move.TargetProjectFileId";
        public const string MoveTargetProjectAlternativeId = "SiInbox.Move.TargetAltId";
        public const string MoveTargetFileName = "SiInbox.Move.TargetFileName";
        public const string MoveTargetAccItemId = "SiInbox.Move.TargetAccItemId";
        public const string MoveTargetAccFolderId = "SiInbox.Move.TargetAccFolderId";
        public const string MoveTargetFilePath = "SiInbox.Move.TargetFilePath";

        public const string LockLockedForEditing = "SiInbox.Lock.LockedForEditing";

        // Source-identity attributes (decided 2026-05-24). Written onto the target ACC
        // item after MoveToProject upload / version, used by future moves to detect
        // same-source identical files and skip redundant re-uploads.
        public const string SourceGmailMessageId = "SiInbox.Source.GmailMessageId";
        public const string SourceMessageDateUtc = "SiInbox.Source.MessageDateUtc";
        public const string SourceOriginalFileName = "SiInbox.Source.OriginalFileName";
        public const string SourceFileSizeBytes = "SiInbox.Source.FileSizeBytes";
        public const string SourceContentSha256 = "SiInbox.Source.ContentSha256";
        public const string SourceAttachmentId = "SiInbox.Source.AttachmentId";

        // Identity attributes (decided 2026-05-28). Canonical message/thread identity
        // written on every ACC file/item produced for an Inbox message.
        public const string IdentityMessageUniqueId   = "SiInbox.Identity.MessageUniqueId";
        public const string IdentityThreadUniqueId    = "SiInbox.Identity.ThreadUniqueId";
        public const string IdentityMessageKey        = "SiInbox.Identity.MessageKey";
        public const string IdentityThreadKey         = "SiInbox.Identity.ThreadKey";
        public const string IdentityInternetMessageId = "SiInbox.Identity.InternetMsgId";
    }
}
