using System.Diagnostics;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Common;
using SiNet.Application.Email.Acc;
using SiNet.Application.Notifications;
using SiNet.Application.Runtime;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Runtime;

/// <summary>
/// Aggregates external health, ACC, Gmail, ACC-ingest background work, startup tasks,
/// and workflow assignee readiness.
/// </summary>
public sealed class RuntimeSubsystemStatusService : IRuntimeSubsystemStatusService, IDisposable
{
    internal const string WorkflowAssigneeConfigMissingTemplate = "workflow-assignee-config-missing";
    internal const string WorkflowAssigneesKey = "workflow-assignees";

    /// <summary>Default upper bound for a single contributor probe, so one unreachable share cannot stall the panel.</summary>
    internal static readonly TimeSpan DefaultContributorTimeout = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _contributorTimeout;
    private readonly IReadOnlyList<ISubsystemStatusContributor> _contributors;
    private IReadOnlyList<SubsystemRuntimeStatus> _contributorRows = [];
    private readonly IExternalHealthCheckSource? _externalHealth;
    private readonly IAccServiceModeProvider? _accMode;
    private readonly IAccServiceHealthProbe? _accHealth;
    private readonly IEmailAccBackgroundWorkTracker? _accIngest;
    private readonly IEnumerable<IConnectorAuthService> _connectors;
    private readonly IStartupTaskRegistry _startupTasks;
    private readonly IWorkflowAssigneeReadinessQueryService? _assigneeReadiness;
    private readonly INotificationDeliveryService? _notifications;
    private readonly object _gate = new();
    private IReadOnlyList<SubsystemRuntimeStatus> _current = [];
    private AccServiceHealthResult? _cachedAccHealth;
    private IReadOnlyList<WorkflowAssigneeReadinessIssueDto>? _cachedAssigneeIssues;
    private bool _assigneeProbeAttempted;

    public RuntimeSubsystemStatusService(
        IStartupTaskRegistry startupTasks,
        IExternalHealthCheckSource? externalHealth = null,
        IAccServiceModeProvider? accMode = null,
        IAccServiceHealthProbe? accHealth = null,
        IEmailAccBackgroundWorkTracker? accIngest = null,
        IEnumerable<IConnectorAuthService>? connectors = null,
        IWorkflowAssigneeReadinessQueryService? assigneeReadiness = null,
        INotificationDeliveryService? notifications = null,
        IEnumerable<ISubsystemStatusContributor>? contributors = null,
        TimeSpan? contributorTimeout = null)
    {
        _startupTasks = startupTasks ?? throw new ArgumentNullException(nameof(startupTasks));
        _contributors = contributors?.ToList() ?? [];
        _contributorTimeout = contributorTimeout ?? DefaultContributorTimeout;
        _externalHealth = externalHealth;
        _accMode = accMode;
        _accHealth = accHealth;
        _accIngest = accIngest;
        _connectors = connectors ?? [];
        _assigneeReadiness = assigneeReadiness;
        _notifications = notifications;

        _startupTasks.Changed += OnDependencyChanged;
        if (_externalHealth is not null)
            _externalHealth.Changed += OnDependencyChanged;
        if (_accIngest is not null)
            _accIngest.ActiveCountChanged += OnAccIngestCountChanged;
        foreach (var c in _connectors)
            c.AuthStateChanged += OnAuthStateChanged;

        Rebuild();
    }

    public IReadOnlyList<SubsystemRuntimeStatus> Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public event EventHandler? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_externalHealth is not null)
            await _externalHealth.RefreshAsync(cancellationToken).ConfigureAwait(false);

        if (_accHealth is not null && _accMode?.Mode == AccServiceMode.Remote)
        {
            try
            {
                _cachedAccHealth = await _accHealth.CheckAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _cachedAccHealth = new AccServiceHealthResult(
                    IsConfigured: true,
                    AccServiceHealthState.Offline,
                    _accMode.BaseUrl,
                    ex.Message);
            }
        }
        else
        {
            _cachedAccHealth = null;
        }

        await RefreshAssigneeReadinessAsync(cancellationToken).ConfigureAwait(false);
        await RefreshContributorsAsync(cancellationToken).ConfigureAwait(false);

        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs every contributor concurrently under its own timeout. A contributor that fails yields a
    /// Degraded row for its own key so one broken probe never removes the rest of the panel.
    /// </summary>
    private async Task RefreshContributorsAsync(CancellationToken cancellationToken)
    {
        if (_contributors.Count == 0)
        {
            _contributorRows = [];
            return;
        }

        var rows = await Task.WhenAll(_contributors.Select(c => RunContributorAsync(c, cancellationToken)))
            .ConfigureAwait(false);
        _contributorRows = rows;
    }

    private async Task<SubsystemRuntimeStatus> RunContributorAsync(
        ISubsystemStatusContributor contributor,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_contributorTimeout);

        try
        {
            return await contributor.ContributeAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailureRow(contributor, $"הבדיקה חרגה מ-{_contributorTimeout.TotalSeconds:0} שניות");
        }
        catch (Exception ex)
        {
            Trace.TraceError(
                "[RuntimeStatus] Contributor '{0}' failed (non-fatal): {1}",
                contributor.Key,
                ex);
            return FailureRow(contributor, $"הבדיקה נכשלה: {ex.Message}");
        }
    }

    private static SubsystemRuntimeStatus FailureRow(ISubsystemStatusContributor contributor, string summary) =>
        new(contributor.Key,
            contributor.DisplayNameHe,
            SubsystemRuntimeState.Degraded,
            ActiveWorkCount: null,
            summary,
            DateTimeOffset.UtcNow);

    public void Dispose()
    {
        _startupTasks.Changed -= OnDependencyChanged;
        if (_externalHealth is not null)
            _externalHealth.Changed -= OnDependencyChanged;
        if (_accIngest is not null)
            _accIngest.ActiveCountChanged -= OnAccIngestCountChanged;
        foreach (var c in _connectors)
            c.AuthStateChanged -= OnAuthStateChanged;
    }

    private async Task RefreshAssigneeReadinessAsync(CancellationToken cancellationToken)
    {
        if (_assigneeReadiness is null)
        {
            _cachedAssigneeIssues = null;
            _assigneeProbeAttempted = false;
            return;
        }

        try
        {
            var issues = await _assigneeReadiness.GetIssuesAsync(cancellationToken).ConfigureAwait(false);
            _cachedAssigneeIssues = issues;
            _assigneeProbeAttempted = true;

            if (issues.Count > 0)
                await NotifyAssigneeConfigMissingAsync(issues, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceError(
                "[RuntimeStatus] Failed to probe workflow assignee readiness (non-fatal): {0}",
                ex);
            _cachedAssigneeIssues = null;
            _assigneeProbeAttempted = true;
        }
    }

    private async Task NotifyAssigneeConfigMissingAsync(
        IReadOnlyList<WorkflowAssigneeReadinessIssueDto> issues,
        CancellationToken cancellationToken)
    {
        if (_notifications is null)
            return;

        try
        {
            await _notifications.DeliverAsync(
                    new NotificationDeliveryRequest(
                        Template: WorkflowAssigneeConfigMissingTemplate,
                        Recipients: Array.Empty<string>(),
                        RawConfigJson: null,
                        ProjectId: null,
                        WorkflowInstanceId: null,
                        TaskId: null,
                        UserId: null),
                    cancellationToken)
                .ConfigureAwait(false);

            Trace.TraceWarning(
                "[RuntimeStatus] Workflow assignee config missing: {0} stage issue(s). First: {1}",
                issues.Count,
                issues[0].SummaryHe);
        }
        catch (Exception ex)
        {
            Trace.TraceError(
                "[RuntimeStatus] Failed to emit workflow-assignee-config-missing notification (non-fatal): {0}",
                ex);
        }
    }

    private void OnDependencyChanged(object? sender, EventArgs e)
    {
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnAccIngestCountChanged(int _)
    {
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnAuthStateChanged(bool _)
    {
        Rebuild();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        var rows = new List<SubsystemRuntimeStatus>();
        var now = DateTimeOffset.UtcNow;

        if (_externalHealth is not null)
        {
            foreach (var h in _externalHealth.Current)
            {
                rows.Add(new SubsystemRuntimeStatus(
                    h.Key,
                    h.DisplayNameHe,
                    h.State,
                    ActiveWorkCount: null,
                    h.SummaryHe,
                    h.LastCheckedUtc));
            }
        }

        // First-wins by key. The legacy bridge is added above, so where a host registers both the
        // bridge and the contributors (V2 hybrid) the bridge keeps rendering and V2 is unchanged.
        foreach (var row in _contributorRows)
            AddIfAbsent(rows, row);

        AddIfAbsent(rows, BuildAccRow(now));
        AddIfAbsent(rows, BuildGmailRow(now));
        AddIfAbsent(rows, BuildAccIngestRow(now));
        AddIfAbsent(rows, BuildWorkflowAssigneesRow(now));

        foreach (var task in _startupTasks.Current)
        {
            // Avoid duplicating keys already covered by dedicated rows.
            if (string.Equals(task.Key, "gmail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(task.Key, "acc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(task.Key, "acc-ingest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(task.Key, WorkflowAssigneesKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rows.Any(r => string.Equals(r.Key, task.Key, StringComparison.OrdinalIgnoreCase)))
                continue;

            rows.Add(new SubsystemRuntimeStatus(
                task.Key,
                task.DisplayNameHe,
                task.State,
                ActiveWorkCount: task.State == SubsystemRuntimeState.Running ? 1 : null,
                task.SummaryHe,
                task.LastChangedUtc));
        }

        lock (_gate)
            _current = rows.OrderBy(r => r.DisplayNameHe, StringComparer.Ordinal).ToList();
    }

    private static void AddIfAbsent(List<SubsystemRuntimeStatus> rows, SubsystemRuntimeStatus row)
    {
        if (rows.Any(r => string.Equals(r.Key, row.Key, StringComparison.OrdinalIgnoreCase)))
            return;

        rows.Add(row);
    }

    private SubsystemRuntimeStatus BuildWorkflowAssigneesRow(DateTimeOffset now)
    {
        if (_assigneeReadiness is null)
        {
            return new SubsystemRuntimeStatus(
                WorkflowAssigneesKey,
                "הקצאות workflow",
                SubsystemRuntimeState.NotConfigured,
                null,
                "שירות מוכנות הקצאות לא רשום",
                now);
        }

        if (!_assigneeProbeAttempted)
        {
            return new SubsystemRuntimeStatus(
                WorkflowAssigneesKey,
                "הקצאות workflow",
                SubsystemRuntimeState.Idle,
                null,
                "טרם נבדק — רענן",
                now);
        }

        var issues = _cachedAssigneeIssues;
        if (issues is null)
        {
            return new SubsystemRuntimeStatus(
                WorkflowAssigneesKey,
                "הקצאות workflow",
                SubsystemRuntimeState.Degraded,
                null,
                "בדיקת הקצאות נכשלה",
                now);
        }

        if (issues.Count == 0)
        {
            return new SubsystemRuntimeStatus(
                WorkflowAssigneesKey,
                "הקצאות workflow",
                SubsystemRuntimeState.Idle,
                null,
                "הקצאות workflow תקינות",
                now);
        }

        var groupCount = issues
            .Select(i => i.GroupCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var hasMissingGroup = issues.Any(i =>
            i.IssueKind is WorkflowAssigneeIssueKind.MissingAssignedGroup
                or WorkflowAssigneeIssueKind.GroupMissing);
        var state = hasMissingGroup
            ? SubsystemRuntimeState.NotConfigured
            : SubsystemRuntimeState.Degraded;
        var summary =
            $"{issues.Count} שלבים ללא assignee ניתן לפתרון" +
            (groupCount > 0 ? $" · {groupCount} קבוצות" : string.Empty);

        return new SubsystemRuntimeStatus(
            WorkflowAssigneesKey,
            "הקצאות workflow",
            state,
            null,
            summary,
            now);
    }

    private SubsystemRuntimeStatus BuildAccRow(DateTimeOffset now)
    {
        if (_accMode is null)
        {
            return new SubsystemRuntimeStatus(
                "acc", "Autodesk ACC", SubsystemRuntimeState.NotConfigured, null,
                "ספק מצב ACC לא רשום", now);
        }

        if (_accMode.Mode == AccServiceMode.Local)
        {
            return new SubsystemRuntimeStatus(
                "acc", "Autodesk ACC", SubsystemRuntimeState.Idle, null,
                "מצב מקומי (Local)", now);
        }

        var probe = _cachedAccHealth;
        if (probe is null)
        {
            return new SubsystemRuntimeStatus(
                "acc", "Autodesk ACC", SubsystemRuntimeState.Idle, null,
                $"Remote — {_accMode.BaseUrl} (טרם נבדק — רענן)", now);
        }

        var state = probe.State switch
        {
            AccServiceHealthState.Online => SubsystemRuntimeState.Idle,
            AccServiceHealthState.NotConfigured => SubsystemRuntimeState.NotConfigured,
            _ => SubsystemRuntimeState.Degraded,
        };
        var summary = probe.IsConfigured
            ? $"Remote — {probe.State}" + (string.IsNullOrWhiteSpace(probe.Detail) ? string.Empty : $": {probe.Detail}")
            : "Remote — לא מוגדר";

        return new SubsystemRuntimeStatus("acc", "Autodesk ACC", state, null, summary, now);
    }

    private SubsystemRuntimeStatus BuildGmailRow(DateTimeOffset now)
    {
        var services = _connectors.ToList();
        if (services.Count == 0)
        {
            return new SubsystemRuntimeStatus(
                "gmail", "Gmail / Google", SubsystemRuntimeState.NotConfigured, null,
                "אין שירות אימות מחובר", now);
        }

        var anyAuth = services.Any(s => s.IsAuthenticated);
        if (!anyAuth)
        {
            return new SubsystemRuntimeStatus(
                "gmail", "Gmail / Google", SubsystemRuntimeState.Stopped, null,
                "לא מחובר", now);
        }

        var email = services.Select(s => s.ConnectedAccountEmail).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
        return new SubsystemRuntimeStatus(
            "gmail", "Gmail / Google", SubsystemRuntimeState.Idle, null,
            string.IsNullOrWhiteSpace(email) ? "מחובר" : $"מחובר — {email}", now);
    }

    private SubsystemRuntimeStatus BuildAccIngestRow(DateTimeOffset now)
    {
        if (_accIngest is null)
        {
            return new SubsystemRuntimeStatus(
                "acc-ingest", "העלאות ACC (דוא״ל)", SubsystemRuntimeState.NotConfigured, null,
                "מעקב עבודת רקע לא רשום", now);
        }

        var count = _accIngest.ActiveCount;
        if (count > 0)
        {
            return new SubsystemRuntimeStatus(
                "acc-ingest", "העלאות ACC (דוא״ל)", SubsystemRuntimeState.Running, count,
                $"תהליכי רקע פעילים: {count}", now);
        }

        return new SubsystemRuntimeStatus(
            "acc-ingest", "העלאות ACC (דוא״ל)", SubsystemRuntimeState.Idle, 0,
            "אין עבודת רקע פעילה", now);
    }
}
