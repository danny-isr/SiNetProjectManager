using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using Xunit;
using Xunit.Abstractions;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Read-only preflight for the certification tier. Must pass before any scenario is allowed to write.
/// <para>
/// It exists so the three write conditions are proven and <em>visible</em> before the first mutation rather
/// than asserted inside a scenario that has already started changing data. It resolves the target, verifies
/// the in-database DEV marker, prints what it is pointing at, and reports the live workflow inventory.
/// </para>
/// <para>
/// Nothing here writes. No secret is printed: the connection string is never echoed, only the server and
/// database parsed out of it, and no token material is touched.
/// </para>
/// </summary>
[Collection(SystemCertificationTestCollection.Name)]
public sealed class SystemCertificationPreflightTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [SystemCertificationFact]
    public async Task Preflight_proves_the_target_is_the_approved_dev_environment()
    {
        var ct = CancellationToken.None;
        var evidence = SystemCertificationEvidence.Create();
        evidence.DeclareAll(
            CertificationRequirement.Required,
            ("preflight.target", "Resolve and authorise the SQL target from environment"),
            ("preflight.marker", "Verify the in-database Certification.Environment marker"),
            ("preflight.connect", "Open a read-only connection to the target"),
            ("preflight.inventory", "Read the active workflow definition inventory"));

        var gmailRequested = SystemCertificationEnvironment.IsLayerRequested(
            SystemCertificationEnvironment.GmailEnabledEnv);
        var accRequested = SystemCertificationEnvironment.IsLayerRequested(
            SystemCertificationEnvironment.AccEnabledEnv);

        evidence.Declare(
            "preflight.gmail",
            gmailRequested ? CertificationRequirement.Required : CertificationRequirement.Optional,
            gmailRequested
                ? "Gmail layer requested — verify configuration without connecting"
                : "Gmail layer not requested for this run");

        evidence.Declare(
            "preflight.acc",
            accRequested ? CertificationRequirement.Required : CertificationRequirement.Optional,
            accRequested
                ? "ACC layer requested — verify configuration without connecting"
                : "ACC layer not requested for this run");

        var target = SystemCertificationEnvironment.TryResolveTarget();

        evidence.Fact("WindowsIdentity", target.WindowsIdentityName ?? "<unknown>");
        evidence.Fact("SqlServer", target.ServerName ?? "<unknown>");
        evidence.Fact("SqlDatabase", target.DatabaseName ?? "<unknown>");

        if (target.Violation is not null)
        {
            evidence.Fail("preflight.target", target.Violation);
            Report(evidence);
            evidence.FinalizeCertification();
            return;
        }

        if (!target.IsAuthorised || target.ConnectionString is null)
        {
            evidence.Fail("preflight.target", "target resolution did not produce an authorised connection.");
            Report(evidence);
            evidence.FinalizeCertification();
            return;
        }

        evidence.Pass(
            "preflight.target",
            $"identity '{target.WindowsIdentityName}', server '{target.ServerName}', database "
            + $"'{target.DatabaseName}', operator SIUser {target.OperatorUserId} — all allowlisted");

        await using var provider = SystemCertificationHost.BuildReadOnly(target.ConnectionString);
        var dbFactory = provider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();

        var marker = await SystemCertificationDatabaseMarker.VerifyAsync(dbFactory, ct);
        evidence.Fact("DatabaseMarker", marker.FoundValue ?? "<absent>");

        if (!marker.IsApproved)
        {
            evidence.Fail("preflight.marker", marker.Violation!);
            Report(evidence);
            evidence.FinalizeCertification();
            return;
        }

        evidence.Pass(
            "preflight.marker",
            $"{SystemCertificationDatabaseMarker.SettingKey} = {marker.FoundValue}");

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var reachable = await db.Database.CanConnectAsync(ct);
            if (!reachable)
            {
                evidence.Fail("preflight.connect", "the target database refused a read-only connection");
                Report(evidence);
                evidence.FinalizeCertification();
                return;
            }
        }

        evidence.Pass("preflight.connect", "read-only connection established");

        // Every active definition must be classified. Left empty on purpose for the preflight: the point is
        // to surface the real inventory so scenarios can be written against it, so an unclassified list here
        // is information rather than a failure.
        var inventory = await WorkflowCoverageInventory.BuildAsync(
            dbFactory,
            SystemCertificationScenarioRegistry.CoverageDispositions,
            ct);

        evidence.Fact("ActiveWorkflowDefinitions", inventory.ActiveDefinitions.Count.ToString());
        evidence.Fact("TotalStages", inventory.TotalStages.ToString());
        evidence.Fact("TotalTransitions", inventory.TotalTransitions.ToString());
        SystemCertificationAssertions.AssertCoverageComplete(inventory, evidence, "preflight.inventory");

        var gmail = SystemCertificationEnvironment.TryResolveGmailLayer();
        evidence.Fact("GmailExpectedAccount", gmail.ExpectedAccount ?? "<not configured>");
        if (gmail.Violation is not null)
        {
            evidence.Fail("preflight.gmail", gmail.Violation);
        }
        else if (gmail.IsEnabled)
        {
            evidence.Pass("preflight.gmail", $"expected mailbox '{gmail.ExpectedAccount}' (no connection attempted)");
        }
        else
        {
            evidence.NotApplicable("preflight.gmail", gmail.SkipReason ?? "Gmail layer not requested");
        }

        var acc = SystemCertificationEnvironment.TryResolveAccLayer(gmail);
        evidence.Fact("AccPlace", acc.PlaceTitle ?? "<not configured>");
        evidence.Fact("AccInboxProject", acc.InboxProjectName ?? "<not configured>");
        if (acc.Violation is not null)
        {
            evidence.Fail("preflight.acc", acc.Violation);
        }
        else if (acc.IsEnabled)
        {
            evidence.Pass(
                "preflight.acc",
                $"place '{acc.PlaceTitle}', inbox project '{acc.InboxProjectName}' (no connection attempted)");
        }
        else
        {
            evidence.NotApplicable("preflight.acc", acc.SkipReason ?? "ACC layer not requested");
        }

        Report(evidence);
        evidence.FinalizeCertification();
    }

    private void Report(SystemCertificationEvidence evidence) =>
        _output.WriteLine($"Certification preflight: {evidence.Verdict}. Evidence: {evidence.MarkdownPath}");
}
