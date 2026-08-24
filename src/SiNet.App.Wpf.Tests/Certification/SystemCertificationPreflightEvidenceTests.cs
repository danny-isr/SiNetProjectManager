using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

public sealed class SystemCertificationPreflightEvidenceTests
{
    [Fact]
    public void TryValidate_fails_when_path_is_missing()
    {
        var previous = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv);
        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, null);

        try
        {
            var violation = SystemCertificationPreflightEvidence.TryValidate(
                CreateTarget(),
                CreateGmail(),
                CreateAcc(),
                out var path);

            Assert.NotNull(violation);
            Assert.Null(path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, previous);
        }
    }

    [Fact]
    public void TryValidate_accepts_bound_certified_json_for_current_runtime()
    {
        if (!TryGetActualGitHead(out var head))
        {
            return;
        }

        var previousEvidence = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv);
        var previousCommit = Environment.GetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv);
        var file = WriteBindingJson(head, DateTimeOffset.Now);

        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, file);
        Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, null);

        try
        {
            var violation = SystemCertificationPreflightEvidence.TryValidate(
                CreateTarget(),
                CreateGmail(),
                CreateAcc(),
                out var path);

            Assert.Null(violation);
            Assert.Equal(file, path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, previousEvidence);
            Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, previousCommit);
            File.Delete(file);
        }
    }

    [Fact]
    public void TryValidate_fails_when_commit_sha_differs()
    {
        if (!TryGetActualGitHead(out var head))
        {
            return;
        }

        var mismatchedSha = head[0] == 'a'
            ? "b" + head[1..]
            : "a" + head[1..];
        var file = WriteBindingJson(mismatchedSha, DateTimeOffset.Now);
        var previousEvidence = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv);
        var previousCommit = Environment.GetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv);

        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, file);
        Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, null);

        try
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.GmailEnabledEnv, "1");
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.AccEnabledEnv, "1");

            var violation = SystemCertificationPreflightEvidence.TryValidate(
                CreateTarget(),
                CreateGmail(),
                CreateAcc(),
                out _);

            Assert.NotNull(violation);
            Assert.Contains("CommitSha", violation, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, previousEvidence);
            Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, previousCommit);
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.GmailEnabledEnv, null);
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.AccEnabledEnv, null);
            File.Delete(file);
        }
    }

    [Fact]
    public void TryValidate_fails_when_preflight_is_stale()
    {
        if (!TryGetActualGitHead(out var head))
        {
            return;
        }

        var file = WriteBindingJson(
            head,
            DateTimeOffset.Now - SystemCertificationPreflightBinding.MaxAge - TimeSpan.FromMinutes(5));
        var previousEvidence = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv);
        var previousCommit = Environment.GetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv);

        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, file);
        Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, null);

        try
        {
            var violation = SystemCertificationPreflightEvidence.TryValidate(
                CreateTarget(),
                CreateGmail(),
                CreateAcc(),
                out _);

            Assert.NotNull(violation);
            Assert.Contains("older than", violation, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, previousEvidence);
            Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, previousCommit);
            File.Delete(file);
        }
    }

    [Fact]
    public void TryValidate_fails_when_preflight_started_in_future()
    {
        if (!TryGetActualGitHead(out var head))
        {
            return;
        }

        var file = WriteBindingJson(
            head,
            DateTimeOffset.Now + SystemCertificationPreflightBinding.MaxFutureSkew + TimeSpan.FromMinutes(10));
        var previousEvidence = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv);
        var previousCommit = Environment.GetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv);

        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, file);
        Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, null);

        try
        {
            var violation = SystemCertificationPreflightEvidence.TryValidate(
                CreateTarget(),
                CreateGmail(),
                CreateAcc(),
                out _);

            Assert.NotNull(violation);
            Assert.Contains("future", violation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, previousEvidence);
            Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, previousCommit);
            File.Delete(file);
        }
    }

    [Fact]
    public void TryValidate_fails_when_env_commit_sha_differs_from_git_head()
    {
        if (!TryGetActualGitHead(out var head))
        {
            return;
        }

        var envSha = head[0] == 'a'
            ? "b" + head[1..]
            : "a" + head[1..];
        var file = WriteBindingJson(head, DateTimeOffset.Now);
        var previousEvidence = Environment.GetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv);
        var previousCommit = Environment.GetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv);

        Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, file);
        Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, envSha);

        try
        {
            var violation = SystemCertificationPreflightEvidence.TryValidate(
                CreateTarget(),
                CreateGmail(),
                CreateAcc(),
                out _);

            Assert.NotNull(violation);
            Assert.Contains("does not match git HEAD", violation, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationEnvironment.PreflightEvidenceEnv, previousEvidence);
            Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, previousCommit);
            File.Delete(file);
        }
    }

    private static bool TryGetActualGitHead(out string head)
    {
        head = string.Empty;
        var previousCommit = Environment.GetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv);
        Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, null);

        try
        {
            var resolution = SystemCertificationGitMetadata.ResolveHeadCommitSha();
            if (resolution.Violation is not null || resolution.Sha is null)
            {
                return false;
            }

            head = resolution.Sha;
            return true;
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemCertificationGitMetadata.CommitShaEnv, previousCommit);
        }
    }

    private static string WriteBindingJson(string commitSha, DateTimeOffset startedAt)
    {
        var file = Path.Combine(Path.GetTempPath(), $"sinet-preflight-{Guid.NewGuid():N}.json");
        var payload = new
        {
            StartedAt = startedAt.ToString("O", CultureInfo.InvariantCulture),
            Verdict = SystemCertificationEvidence.CertifiedVerdict,
            Facts = new Dictionary<string, string>
            {
                [SystemCertificationPreflightBinding.FactCommitSha] = commitSha,
                [SystemCertificationPreflightBinding.FactSqlServer] = ".",
                [SystemCertificationPreflightBinding.FactSqlDatabase] = "SystemCertificationTest",
                [SystemCertificationPreflightBinding.FactWindowsIdentity] = Environment.UserName,
                [SystemCertificationPreflightBinding.FactOperatorUserId] = "1",
                [SystemCertificationPreflightBinding.FactDatabaseMarker] = SystemCertificationDatabaseMarker.RequiredValue,
                [SystemCertificationPreflightBinding.FactGmailExpectedAccount] = "test@example.com",
                [SystemCertificationPreflightBinding.FactAccPlace] = SystemCertificationEnvironment.RequiredAccPlaceTitle,
                [SystemCertificationPreflightBinding.FactAccInboxProject] = "SYS-CERT-INBOX",
            },
        };

        File.WriteAllText(file, JsonSerializer.Serialize(payload));
        return file;
    }

    private static SystemCertificationEnvironment.Target CreateTarget() =>
        new(
            IsEnabled: true,
            SkipReason: null,
            Violation: null,
            ConnectionString: "Server=.;Database=SystemCertificationTest;Trusted_Connection=True;",
            ServerName: ".",
            DatabaseName: "SystemCertificationTest",
            WindowsIdentityName: Environment.UserName,
            OperatorUserId: 1);

    private static SystemCertificationEnvironment.GmailLayer CreateGmail() =>
        new(true, null, null, "test@example.com");

    private static SystemCertificationEnvironment.AccLayer CreateAcc() =>
        new(true, null, null, SystemCertificationEnvironment.RequiredAccPlaceTitle, "SYS-CERT-INBOX");
}
