namespace SiNetSQL.Models;

/// <summary>
/// Status enum for email inbox message processing.
/// ⚠️ WARNING: Do NOT change the order or values of existing entries.
/// New statuses must be added at the end with explicit values.
/// 
/// Lease-based processing flow:
///   Pending → Processing → Uploaded → Moved
///                ↓
///              Error
/// 
/// The Processing state acts as a lease: if a worker crashes, 
/// another worker can reclaim messages stuck in Processing (TTL expired).
/// </summary>
public enum EmailInboxStatus
{
    /// <summary>
    /// Message received but not yet picked up for processing.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Message is currently being processed (lease acquired).
    /// ProcessingByLogin and ProcessingStartedAtUtc track the lease holder.
    /// If TTL expires, another worker may reclaim this message.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// All attachments successfully uploaded to ACC Inbox folder.
    /// </summary>
    Uploaded = 2,

    /// <summary>
    /// Message has been assigned to a real project and files copied to project folder.
    /// </summary>
    Moved = 3,

    /// <summary>
    /// An error occurred during processing.
    /// See Error column for details.
    /// </summary>
    Error = 4
}
