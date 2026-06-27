namespace SiNetSQL.Models;

/// <summary>
/// Indicates how a ProjectFileInstance was created.
/// Used for auditing and lifecycle tracking.
/// </summary>
public enum FileInstanceSourceType
{
    /// <summary>
    /// Created manually (drag-drop, new from template, etc.)
    /// </summary>
    Manual = 0,

    /// <summary>
    /// Placed from an email attachment via the Email Management panel.
    /// The SourceEmailAttachmentId FK points to the originating attachment.
    /// </summary>
    EmailAttachment = 1,

    /// <summary>
    /// Created from a project file template.
    /// </summary>
    Template = 2
}
