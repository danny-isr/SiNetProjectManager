using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Offline proof that the certification evidence gate actually fails a run. Without these, the gate would
/// rest on the same assumption that made the L4W evidence writer misleading: that recording a failure and
/// failing the test are the same thing. They are not — in <c>PilotSmokeEvidence</c> a <c>Fail</c> row only
/// appends text.
/// </summary>
public sealed class SystemCertificationEvidenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "sinet-cert-evidence-tests",
        Guid.NewGuid().ToString("N"));

    private SystemCertificationEvidence NewEvidence() =>
        SystemCertificationEvidence.Create(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void WhenRequiredStepIsLeftNotRunThenCertificationFails()
    {
        var evidence = NewEvidence();
        evidence.Declare("prp.start", CertificationRequirement.Required, "Start the Proposal workflow");

        Assert.Throws<SystemCertificationFailedException>(evidence.FinalizeCertification);
    }

    [Fact]
    public void WhenRequiredStepFailsThenCertificationFails()
    {
        var evidence = NewEvidence();
        evidence.Declare("prp.start", CertificationRequirement.Required, "Start the Proposal workflow");
        evidence.Fail("prp.start", "workflow did not reach PRP.ProjectSetup");

        Assert.Throws<SystemCertificationFailedException>(evidence.FinalizeCertification);
    }

    [Fact]
    public void WhenOptionalStepFailsThenCertificationStillCompletes()
    {
        var evidence = NewEvidence();
        evidence.Declare("acc.readback", CertificationRequirement.Optional, "ACC read-back");
        evidence.Fail("acc.readback", "ACC layer not enabled for this run");

        evidence.FinalizeCertification();
    }

    [Fact]
    public void WhenEveryRequiredStepPassesThenVerdictIsCertified()
    {
        var evidence = NewEvidence();
        evidence.Declare("prp.start", CertificationRequirement.Required, "Start the Proposal workflow");
        evidence.Pass("prp.start", "instance 41 reached PRP.ProjectSetup");

        Assert.Equal("CERTIFIED", evidence.Verdict);
    }

    [Fact]
    public void WhenRequiredStepIsBlockedThenVerdictIsNotCertified()
    {
        var evidence = NewEvidence();
        evidence.Declare("prp.sendquote", CertificationRequirement.Required, "Send quote to client");
        evidence.Blocked("prp.sendquote", "sending real email is out of scope by policy");

        Assert.Equal("NOT CERTIFIED", evidence.Verdict);
    }

    [Fact]
    public void WhenRequiredStepIsBlockedThenCertificationFails()
    {
        // A green test process alongside a report that says NOT CERTIFIED is the precise failure mode this
        // tier exists to remove, so Blocked must fail the gate even though it was analysed in advance.
        var evidence = NewEvidence();
        evidence.Declare("prp.sendquote", CertificationRequirement.Required, "Send quote to client");
        evidence.Blocked("prp.sendquote", "sending real email is out of scope by policy");

        Assert.Throws<SystemCertificationFailedException>(evidence.FinalizeCertification);
    }

    [Fact]
    public void WhenRequiredStepIsNotApplicableThenVerdictIsCertified()
    {
        var evidence = NewEvidence();
        evidence.Declare("acc.readback", CertificationRequirement.Required, "ACC read-back");
        evidence.NotApplicable("acc.readback", "ACC layer deliberately excluded from this run");

        Assert.Equal(SystemCertificationEvidence.CertifiedVerdict, evidence.Verdict);
    }

    [Fact]
    public void WhenRequiredStepIsBlockedThenReportIsStillWritten()
    {
        // Audit and gate are separate: the report must survive a failing run, otherwise a failure would
        // destroy the evidence explaining it.
        var evidence = NewEvidence();
        evidence.Declare("prp.sendquote", CertificationRequirement.Required, "Send quote to client");
        evidence.Blocked("prp.sendquote", "sending real email is out of scope by policy");

        Assert.Throws<SystemCertificationFailedException>(evidence.FinalizeCertification);

        Assert.Contains("NOT CERTIFIED", File.ReadAllText(evidence.MarkdownPath), StringComparison.Ordinal);
    }

    [Fact]
    public void WhenStepIsRecordedWithoutBeingDeclaredThenItThrows()
    {
        var evidence = NewEvidence();

        Assert.Throws<InvalidOperationException>(() => evidence.Pass("never.declared", "detail"));
    }

    [Fact]
    public void WhenSettingRestoreIsVerifiedWithoutASnapshotThenItThrows()
    {
        var evidence = NewEvidence();

        Assert.Throws<InvalidOperationException>(() =>
            evidence.SettingRestoreVerified("Pilot.Enabled", "true", matchesOriginal: true));
    }
}
