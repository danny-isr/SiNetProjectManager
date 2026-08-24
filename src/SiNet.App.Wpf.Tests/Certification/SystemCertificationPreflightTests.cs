using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Infrastructure.Sql;
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
        evidence.DeclareAll(
            CertificationRequirement.Optional,
            ("preflight.gmail", "Report the expected Gmail mailbox"),
            ("preflight.acc", "Report the ACC place and inbox project"));

        var target = SystemCertificationEnvironment.TryResolveTarget();

        evidence.Fact("WindowsIdentity", target.WindowsIdentityName);
        evidence.Fact("SqlServer", target.ServerName);
        evidence.Fact("SqlDatabase", target.DatabaseName);

        if (target.Violation is not null)
        {
            evidence.Fail("preflight.target", target.Violation);
            Report(evidence);
            evidence.FinalizeCertification();
            return;
        }

        evidence.Pass(
            "preflight.target",
            $"identity '{target.WindowsIdentityName}', server '{target.ServerName}', database "
            + $"'{target.DatabaseName}', operator SIUser {target.OperatorUserId} — all allowlisted");

        await using var provider = BuildReadOnlyProvider(target.ConnectionString!);
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
            new Dictionary<string, (WorkflowCoverageInventory.Classification, string)>(StringComparer.Ordinal),
            ct);

        evidence.Fact("ActiveWorkflowDefinitions", inventory.ActiveDefinitions.Count.ToString());
        evidence.Fact("TotalStages", inventory.TotalStages.ToString());
        evidence.Fact("TotalTransitions", inventory.TotalTransitions.ToString());
        evidence.Pass("preflight.inventory", WorkflowCoverageInventory.Describe(inventory));

        var gmail = SystemCertificationEnvironment.TryResolveGmailLayer();
        evidence.Fact("GmailExpectedAccount", gmail.ExpectedAccount ?? "<not configured>");
        if (gmail.IsEnabled)
        {
            evidence.Pass("preflight.gmail", $"expected mailbox '{gmail.ExpectedAccount}'");
        }
        else
        {
            evidence.NotApplicable("preflight.gmail", gmail.SkipReason ?? "Gmail layer not enabled");
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
                $"place '{acc.PlaceTitle}', inbox project '{acc.InboxProjectName}'");
        }
        else
        {
            evidence.NotApplicable("preflight.acc", acc.SkipReason ?? "ACC layer not enabled");
        }

        Report(evidence);
        evidence.FinalizeCertification();
    }

    /// <summary>
    /// Minimal read-only composition: SQL and settings only. The Google and ACC modules are deliberately
    /// absent so a preflight cannot touch an external system even by accident.
    /// </summary>
    private static ServiceProvider BuildReadOnlyProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddSiNetSql(connectionString);
        services.AddSiNetSystemSettingsSql();
        return services.BuildServiceProvider();
    }

    private void Report(SystemCertificationEvidence evidence) =>
        _output.WriteLine($"Certification preflight: {evidence.Verdict}. Evidence: {evidence.MarkdownPath}");
}
