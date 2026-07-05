namespace SiNet.Application.Actions;

/// <summary>
/// Stable process-action codes for the Application-layer action port. This catalog is intentionally
/// minimal in the foundation slice; legacy SiNetSQL action metadata remains reference-only until
/// handlers migrate one-by-one.
/// </summary>
public static class ProcessActionCodes
{
    /// <summary>Workflow transition action: send a notification (safe no-op handler in foundation slice).</summary>
    public const string SendNotification = nameof(SendNotification);

    /// <summary>Workflow transition action: record a task result on stage-linked tasks.</summary>
    public const string RecordTaskResult = nameof(RecordTaskResult);

    /// <summary>Workflow transition action: set broad project status from config.</summary>
    public const string SetProjectStatus = nameof(SetProjectStatus);

    /// <summary>Workflow transition marker: stage tasks are provisioned by the orchestrator.</summary>
    public const string CreateStageTasks = nameof(CreateStageTasks);

    /// <summary>Workflow transition convenience: set project status to BillingPending.</summary>
    public const string SetBillingPending = nameof(SetBillingPending);
}
