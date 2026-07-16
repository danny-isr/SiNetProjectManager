using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Workflow;
using SiNetSQL.Domain.Actions;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Native replacement for <c>SiNetSQL.Services.Workflow.WorkflowActionLifecycleReporter</c>.
/// Bridges Action lifecycle <c>Completed</c> events to the single native workflow engine via the
/// Application write port <see cref="IWorkflowCommandService"/>
/// (<see cref="IWorkflowCommandService.CheckAndAdvanceOnActionCompletedAsync"/>), so email-driven
/// workflow-advancing actions (e.g. <c>ApproveOrClose</c> / <c>CloseOpinion</c>) advance through the
/// native orchestrator instead of the legacy <c>WorkflowActionCompletedHandler</c>.
/// <para>
/// Safety conditions are preserved exactly from the legacy reporter (all must hold; otherwise the
/// reporter silently skips):
/// <list type="number">
///   <item>Only <see cref="ReportCompletedAsync"/> forwards (Started/Failed/Cancelled/Deferred/NoOp are no-ops).</item>
///   <item><c>Context != null</c>.</item>
///   <item><c>Context.WorkflowInstanceId != null</c> (explicit — never inferred from ProjectId / EmailMessageId).</item>
///   <item><c>ActionCode</c> is not empty.</item>
///   <item><see cref="ActionDefinitionRegistry.TryGet"/> succeeds for the code.</item>
///   <item><see cref="ActionDefinition.CanAdvanceWorkflow"/> is <c>true</c>.</item>
/// </list>
/// </para>
/// <para>
/// Exceptions raised by the engine are logged and swallowed so that <c>ActionExecutor</c> behavior is
/// not affected by an optional bridge failure — identical to the legacy reporter.
/// </para>
/// </summary>
public sealed class NativeWorkflowActionLifecycleReporter(
    IWorkflowCommandService workflowCommands) : IActionLifecycleReporter
{
    private readonly IWorkflowCommandService _workflowCommands = workflowCommands
        ?? throw new ArgumentNullException(nameof(workflowCommands));

    public async ValueTask ReportCompletedAsync(
        ActionExecutionContext context,
        string outcome,
        string? message = null,
        IReadOnlyDictionary<string, object?>? data = null,
        CancellationToken cancellationToken = default)
    {
        // Safety gates — silent skips, no exceptions (parity with the legacy reporter).
        if (context is null) return;
        if (context.WorkflowInstanceId is not int instanceId || instanceId <= 0)
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("LifecycleReporter.Completed",
                $"action={context?.ActionCode} SKIP — no WorkflowInstanceId (context {(context is null ? "null" : "present")})");
            return;
        }
        if (string.IsNullOrWhiteSpace(context.ActionCode))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("LifecycleReporter.Completed", $"instance={instanceId} SKIP — empty ActionCode");
            return;
        }

        if (!ActionDefinitionRegistry.TryGet(context.ActionCode, out var definition))
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("LifecycleReporter.Completed",
                $"instance={instanceId} action={context.ActionCode} SKIP — no ActionDefinition registered");
            return;
        }

        if (!definition.CanAdvanceWorkflow)
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("LifecycleReporter.Completed",
                $"instance={instanceId} action={context.ActionCode} SKIP — CanAdvanceWorkflow=false");
            return;
        }

        try
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("LifecycleReporter.Completed",
                $"instance={instanceId} action={context.ActionCode} outcome={outcome} → advancing via native command");
            await _workflowCommands.CheckAndAdvanceOnActionCompletedAsync(
                new ActionCompletedCommand(
                    instanceId,
                    context.ActionCode,
                    outcome,
                    context.UserId ?? 0),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Do not let workflow advance failures break ActionExecutor.
            AppLogger.ErrorWithContext(
                ex,
                "[NativeWorkflowActionLifecycleReporter] CheckAndAdvanceOnActionCompletedAsync failed",
                new
                {
                    ActionCode = context.ActionCode,
                    context.WorkflowInstanceId,
                    context.ProjectId,
                    context.UserId,
                    Outcome = outcome,
                });
        }
    }

    // ─── No-op for non-Completed lifecycle events ────────────────────────

    public ValueTask ReportStartedAsync(
        ActionExecutionContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask ReportFailedAsync(
        ActionExecutionContext context,
        Exception? exception = null,
        string? message = null,
        IReadOnlyDictionary<string, object?>? data = null,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask ReportCancelledAsync(
        ActionExecutionContext context,
        string? message = null,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask ReportDeferredAsync(
        ActionExecutionContext context,
        string? message = null,
        IReadOnlyDictionary<string, object?>? data = null,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask ReportNoOpAsync(
        ActionExecutionContext context,
        string? message = null,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
