namespace SiNet.Application.Projects;



/// <summary>

/// Per-mailbox Gmail leaf rename to current <c>NameAndNumber</c> when

/// <c>Email.AutoSyncProjectLabelNames</c> is on. Identity = <c>^(Number)</c> on the leaf.

/// </summary>

public interface IProjectGmailLabelSyncService

{

    /// <param name="force">When true, sync even if the system setting is off (manual command).</param>

    Task<ProjectGmailLabelSyncResult> SyncAsync(

        bool force = false,

        CancellationToken cancellationToken = default);



    /// <summary>

    /// Keeps <paramref name="keepLabelId"/> and deletes other project leaf labels that share

    /// the same <paramref name="projectNumber"/> in the current mailbox. Does not rename;

    /// call <see cref="SyncAsync"/> afterwards so the survivor matches <c>NameAndNumber</c>.

    /// </summary>

    Task<ProjectGmailLabelDuplicateResolveResult> ResolveDuplicateLeavesAsync(

        int projectNumber,

        string keepLabelId,

        CancellationToken cancellationToken = default);

}


