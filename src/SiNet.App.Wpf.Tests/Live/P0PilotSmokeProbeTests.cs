using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;
using SiNet.Application.Settings;
using SiNet.Infrastructure.AccBootstrap;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.AutodeskLocal;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using Xunit;
using Xunit.Abstractions;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// Read-only pre-flight for the L4W write smoke. Prints the resolved targets so the operator can
/// confirm them <b>before</b> anything is written, and reports which preconditions the write run
/// would find.
/// <para>
/// Writes nothing: no SystemSettings change, no project, no Gmail label, no ACC call that mutates.
/// Its own category so <c>-Probe</c> can select it without selecting the write scenario.
/// </para>
/// </summary>
[Trait("Category", ProbeCategory)]
public sealed class P0PilotSmokeProbeTests(ITestOutputHelper output)
{
    internal const string ProbeCategory = "PilotSmokeProbe";

    private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));

    [PilotSmokeFact]
    public async Task Probe_prints_the_resolved_write_targets_without_writing()
    {
        var gate = PilotSmokeEnvironment.TryResolveSqlTier();
        Assert.True(gate.IsEnabled, gate.SkipReason);

        var gmail = PilotSmokeEnvironment.TryResolveGmailTier();
        var acc = PilotSmokeEnvironment.TryResolveAccTier(gmail);

        Line("=== P0 Pilot smoke probe (read-only) ===");
        Line($"SQL server              : {gate.ServerName}");
        Line($"SQL database            : {gate.DatabaseName}");
        Line($"Operator SIUser.Id      : {gate.OperatorUserId}");
        Line($"Gmail layer             : {(gmail.IsEnabled ? "ON" : $"OFF — {gmail.SkipReason}")}");
        if (gmail.IsEnabled)
        {
            Line($"Gmail subject token     : {gmail.SubjectToken}");
            Line($"Gmail expected mailbox  : {gmail.ExpectedAccount}");
        }

        Line($"ACC layer               : {(acc.IsEnabled ? "ON" : $"OFF — {acc.SkipReason}")}");
        if (acc.IsEnabled)
        {
            Line($"ACC inbox project       : {acc.InboxProjectName}");
            Line($"ACC place               : {acc.PlaceTitle}");
        }

        var services = new ServiceCollection();
        services.AddSiNetLogging();
        services.AddSiNetSql(gate.ConnectionString!);
        services.AddSiNetAuthorizationSql();
        services.AddSiNetSystemSettingsSql();
        await using var provider = services.BuildServiceProvider();

        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var canConnect = await db.Database.CanConnectAsync();
        Line($"Database reachable      : {canConnect}");
        Assert.True(canConnect, "Cannot connect to the declared database.");

        var operatorUser = await db.Siusers
            .AsNoTracking()
            .Where(u => u.Id == gate.OperatorUserId)
            .Select(u => new { u.Name, u.IsActive })
            .FirstOrDefaultAsync();
        Line($"Operator resolves to    : {(operatorUser is null
            ? "MISSING"
            : $"'{operatorUser.Name}' active={operatorUser.IsActive}")}");

        var settings = await provider.GetRequiredService<ISystemSettingsQueryService>()
            .GetSystemSettingsAsync();

        Line($"Pilot.Enabled (current) : {settings.Workflow.PilotEnabled}");
        Line($"Pilot.AllowedUserIds    : '{settings.Workflow.PilotAllowedUserIds}'");
        Line($"Pilot.AllowedWfCodes    : '{settings.Workflow.PilotAllowedWorkflowCodes}'");

        // The single most dangerous value on a DEV database restored from production: it names the
        // ACC project the office Inbox ingest writes into (docs/ENVIRONMENTS.md §5.1.1).
        Line($"InboxProjectName        : '{settings.EmailOffice.InboxProjectName}'");
        if (acc.IsEnabled)
        {
            var matches = string.Equals(
                settings.EmailOffice.InboxProjectName?.Trim(),
                acc.InboxProjectName,
                StringComparison.Ordinal);
            Line($"  → matches smoke inbox : {matches} "
                + (matches
                    ? "(the write run will not need to change it)"
                    : "(the write run will temporarily switch it, then restore)"));
        }

        var siPlaceId = await db.Places
            .AsNoTracking()
            .Where(p => p.Title == PilotSmokeEnvironment.RequiredAccPlaceTitle)
            .Select(p => p.Id)
            .FirstOrDefaultAsync();
        Line($"Place '{PilotSmokeEnvironment.RequiredAccPlaceTitle}' exists         : "
            + (siPlaceId == 0
                ? "no — the write run will create it (docs/TEST_STRATEGY.md §4W.2.2)"
                : $"yes (id={siPlaceId})"));

        var windowsLogin = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var loginOwner = await db.Siusers
            .AsNoTracking()
            .Where(u => u.IsActive && u.LoginName == windowsLogin)
            .Select(u => new { u.Id, u.Name })
            .FirstOrDefaultAsync();
        Line($"Windows login           : {windowsLogin}");
        Line($"  → resolves to SIUser  : {(loginOwner is null
            ? $"nobody — the write run will repoint SIUser {gate.OperatorUserId} (§4W.2.3)"
            : $"{loginOwner.Id} '{loginOwner.Name}'"
              + (loginOwner.Id == gate.OperatorUserId
                  ? " (matches the declared operator)"
                  : $" *** DOES NOT MATCH declared {gate.OperatorUserId} — the write run will refuse ***"))}");

        var proposalId = await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.Code == WorkflowCodes.Proposal && d.IsActive)
            .Select(d => d.Id)
            .FirstOrDefaultAsync();
        var planningId = await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.Code == WorkflowCodes.PlanningWorkflow && d.IsActive)
            .Select(d => d.Id)
            .FirstOrDefaultAsync();
        Line($"{WorkflowCodes.Proposal} definition id  : {(proposalId == 0 ? "MISSING" : proposalId.ToString())}");
        Line($"{WorkflowCodes.PlanningWorkflow} definition id   : {(planningId == 0 ? "MISSING" : planningId.ToString())}");

        var planningJobTypeId = planningId == 0
            ? 0
            : await db.ProjectTypeWorkflowDefinitions
                .AsNoTracking()
                .Where(m => m.WorkflowDefinitionId == planningId && m.IsEnabled)
                .OrderBy(m => m.SortOrder)
                .Select(m => m.ProjectTypeId)
                .FirstOrDefaultAsync();
        Line($"JobType → PLN mapping   : "
            + (planningJobTypeId == 0 ? "MISSING — S7 would pass trivially" : $"project type {planningJobTypeId}"));

        var smokeRows = await db.Projects
            .AsNoTracking()
            .CountAsync(p => p.Title!.StartsWith(PilotSmokeEnvironment.SmokeTitlePrefix));
        Line($"Existing smoke projects : {smokeRows} (left from previous runs; not deleted by design)");

        await ProbeGmailAndAccAsync(gate, gmail, acc);

        Line("=== Probe complete — nothing was written ===");
    }

    /// <summary>
    /// Read-only probe of the two external systems. Silent token restore only, and the ACC probe is
    /// the diagnostics endpoint — neither creates nor modifies anything.
    /// </summary>
    private async Task ProbeGmailAndAccAsync(
        PilotSmokeEnvironment.SqlTier gate,
        PilotSmokeEnvironment.GmailTier gmail,
        PilotSmokeEnvironment.AccTier acc)
    {
        if (!gmail.IsEnabled && !acc.IsEnabled)
        {
            Line("Gmail / ACC probe        : skipped (both layers off)");
            return;
        }

        // The same graph the write run builds, so resolving cleanly here is evidence the write run
        // will resolve too. The guard is inert in a probe: nothing calls an ACC write port.
        await using var provider = PilotSmokeHost.Build(
            gate.ConnectionString!,
            acc,
            new PilotSmokeAccGuard(),
            includeProcessBackbone: false);

        if (gmail.IsEnabled)
        {
            var auth = provider.GetRequiredService<IConnectorAuthService>();
            var restored = await auth.TryRestoreSessionAsync();
            Line($"Gmail silent restore    : {restored}");

            if (restored)
            {
                await auth.RefreshAccountProfileAsync();
                var connected = auth.ConnectedAccountEmail;
                var matches = string.Equals(
                    connected?.Trim(), gmail.ExpectedAccount, StringComparison.OrdinalIgnoreCase);
                Line($"Gmail authenticated as  : {connected ?? "<unknown>"}");
                Line($"  → matches declared    : {matches}"
                    + (matches ? string.Empty : "  *** THE WRITE RUN WILL ABORT ***"));

                var gateway = provider.GetRequiredService<IEmailGateway>();
                try
                {
                    var chosen = await PilotSmokeGmailMessagePicker.ResolveAsync(gateway, gmail.SubjectToken);
                    Line($"Test message selected   : auto/subject mode='{chosen.SelectionMode}'");
                    Line($"  attachments           : {chosen.AttachmentCount}");
                    Line($"  subject               : '{chosen.Subject}'");
                    Line($"  messageId             : {chosen.MessageId}");
                }
                catch (InvalidOperationException ex)
                {
                    Line($"Test message selected   : FAILED — {ex.Message}");
                }
            }
            else
            {
                Line("  *** no stored token — sign in interactively in the app once, then re-run ***");
            }
        }

        if (!acc.IsEnabled)
        {
            return;
        }

        var mode = provider.GetService<IAccServiceModeProvider>();
        Line($"ACC mode                : {mode?.Mode.ToString() ?? "<unknown>"} (base '{mode?.BaseUrl}')");

        var localExecutor = provider.GetService<IAccInboxBootstrapLocalExecutor>();
        Line($"Local inbox executor    : {(localExecutor is null ? "NOT registered" : "registered")}");
        Line("  note                  : the write run pins the inbox bootstrap to the LOCAL executor, "
            + "because in Remote mode AccService reads InboxProjectName from its own database "
            + "(docs/ENVIRONMENTS.md §5.1.1).");

        if (mode?.Mode == AccServiceMode.Local)
        {
            Line("AccService diag         : skipped — mode is Local, so privileged ACC work runs "
                + "in-process and the remote endpoint is irrelevant.");
            await ProbeAutodeskDirectlyAsync(provider, acc);
            return;
        }

        var probe = provider.GetService<IAccServiceDiagnosticsProbe>();
        if (probe is null)
        {
            Line("AccService diag         : IAccServiceDiagnosticsProbe not registered");
            return;
        }

        var diag = await probe.ProbeAsync();
        Line($"AccService reachable    : {diag.Reachable}");
        Line($"AccService hasApiKey    : {diag.HasApiKey} (source '{diag.KeySource}')");
        Line($"AccService autodeskOk   : {diag.AutodeskOk} — {diag.AutodeskDetail}");
        Line($"AccService dbOk         : {diag.DbOk} — {diag.DbDetail}");
    }

    /// <summary>
    /// In Local mode the only thing that matters is whether this process can talk to Autodesk. Uses
    /// the read-only discovery port and reports whether the three project names in play already
    /// exist, so the operator knows in advance what the write run will create versus reuse.
    /// </summary>
    private async Task ProbeAutodeskDirectlyAsync(
        IServiceProvider provider,
        PilotSmokeEnvironment.AccTier acc)
    {
        var discovery = provider.GetService<IAccLiveProjectDiscoveryService>();
        if (discovery is null)
        {
            Line("Autodesk reachability   : IAccLiveProjectDiscoveryService not registered");
            return;
        }

        IReadOnlyList<AccHubCatalogEntry> hubs;
        try
        {
            hubs = await discovery.GetHubsAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            Line($"Autodesk reachability   : FAILED — {ex.GetType().Name}: {ex.Message}");
            Line("  *** the ACC layer cannot run; check the Autodesk credentials in the vault "
                + "(docs/OPS_ACCSERVICE_TOKEN_REFRESH.md) ***");
            return;
        }

        Line($"Autodesk reachability   : OK — {hubs.Count} hub(s)");
        foreach (var hub in hubs)
        {
            Line($"  hub                   : {hub.DisplayText}");
        }

        if (hubs.Count == 0)
        {
            Line("  *** no hubs — the ACC layer cannot run ***");
            return;
        }

        var expectedFilingProject = "SI-" + PilotSmokeEnvironment.RequiredAccPlaceTitle;
        foreach (var hub in hubs)
        {
            IReadOnlyList<AccProjectCatalogEntry> projects;
            try
            {
                projects = await discovery.GetProjectsAsync(hub.HubId);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                Line($"  projects in '{hub.DisplayName}'  : FAILED — {ex.Message}");
                continue;
            }

            Line($"  projects in '{hub.DisplayName}'  : {projects.Count}");
            foreach (var name in new[] { acc.InboxProjectName!, expectedFilingProject })
            {
                var hit = projects.FirstOrDefault(p =>
                    string.Equals(p.DisplayName?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                Line($"    '{name}'".PadRight(26)
                    + $": {(hit is null ? "does not exist — the write run will create it" : $"exists ({hit.ProjectId})")}");
            }
        }
    }

    private void Line(string text) => _output.WriteLine(text);
}
