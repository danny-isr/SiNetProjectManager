using System.IO;
using System.Text.RegularExpressions;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Autodesk;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

public sealed class AccServiceTokenDistributionTests
{
    [Fact]
    public void Admin_api_probe_targets_construction_admin_list_projects()
    {
        Assert.Contains(
            "construction/admin/v1/accounts/",
            AccServiceAdminApiProbe.AbsoluteUrlTemplate,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limit=1", AccServiceAdminApiProbe.AbsoluteUrlTemplate, StringComparison.Ordinal);
    }

    [Fact]
    public void Dedicated_AccService_path_accepted_generic_desktop_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "SiNetDist-" + Guid.NewGuid().ToString("N"));
        var servicePath = Path.Combine(root, "SiNet", "Autodesk", "AccService", "refresh_token.json");
        var desktopPath = Path.Combine(root, "SiNet", "Autodesk", "refresh_token.json");
        Directory.CreateDirectory(Path.GetDirectoryName(servicePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(desktopPath)!);
        File.WriteAllText(servicePath, "{}");
        File.WriteAllText(desktopPath, "{}");

        try
        {
            Assert.True(AccServiceTokenPackageMeta.IsDedicatedAccServiceTokenPath(servicePath));
            Assert.False(AccServiceTokenPackageMeta.IsGenericDesktopTokenPath(servicePath));
            Assert.True(AccServiceTokenPackageMeta.IsGenericDesktopTokenPath(desktopPath));
            Assert.False(AccServiceTokenPackageMeta.IsDedicatedAccServiceTokenPath(desktopPath));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Package_with_matching_siad_is_accepted()
    {
        var text = AccServiceTokenPackageMeta.Format(
            SystemSettingsDefaults.AccBootstrapAdminEmail,
            "SIAD@si-eng.co.il",
            "user-1",
            "DEV-PC",
            DateTimeOffset.UtcNow,
            sourcePath: @"C:\Users\x\AppData\Local\SiNet\Autodesk\AccService\refresh_token.json");

        var dto = AccServiceTokenPackageMeta.Parse(text);
        var result = AccServiceTokenPackageMeta.ValidateForInstall(
            dto,
            SystemSettingsDefaults.AccBootstrapAdminEmail);

        Assert.True(result.Accepted);
        Assert.Equal("AccServiceAdmin", dto.TokenPurpose);
    }

    [Fact]
    public void Package_with_danny_actual_is_rejected()
    {
        var text = AccServiceTokenPackageMeta.Format(
            "siad@si-eng.co.il",
            "danny@si-eng.co.il",
            null,
            "DEV-PC",
            DateTimeOffset.UtcNow);

        var result = AccServiceTokenPackageMeta.ValidateForInstall(
            AccServiceTokenPackageMeta.Parse(text),
            "siad@si-eng.co.il");

        Assert.False(result.Accepted);
        Assert.Contains("does not match", result.Reason ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Package_wrong_purpose_is_rejected()
    {
        var text = "TokenPurpose=UserContext\nExpectedAdminEmail=siad@si-eng.co.il\nActualAdminEmail=siad@si-eng.co.il\n";
        var result = AccServiceTokenPackageMeta.ValidateForInstall(AccServiceTokenPackageMeta.Parse(text));
        Assert.False(result.Accepted);
    }

    [Fact]
    public void Health_store_check_rejects_desktop_path()
    {
        Assert.False(AccAdminIdentityStatusContributor.IsDedicatedAccServiceStore(
            "AccServiceAdmin",
            @"C:\Users\x\AppData\Local\SiNet\Autodesk\refresh_token.json"));
        Assert.True(AccAdminIdentityStatusContributor.IsDedicatedAccServiceStore(
            "AccServiceAdmin",
            @"C:\Users\x\AppData\Local\SiNet\Autodesk\AccService\refresh_token.json"));
    }

    [Fact]
    public void Identity_siad_vs_siad_healthy_danny_mismatch()
    {
        Assert.Equal(
            AccServiceAdminIdentityStatus.Healthy,
            AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "siad@si-eng.co.il", adminApiStatus: "200").Status);
        Assert.Equal(
            AccServiceAdminIdentityStatus.AdminEmailMismatch,
            AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "danny@si-eng.co.il").Status);
        Assert.Equal(
            AccServiceAdminIdentityStatus.AdminApiUnauthorized,
            AccServiceAdminIdentity.WithAdminApiStatus(
                AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "siad@si-eng.co.il"),
                "403").Status);
        Assert.Equal(
            AccServiceAdminIdentityStatus.ServiceUnavailable,
            AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "siad@si-eng.co.il").Status);
    }

    [Fact]
    public void Export_and_Install_scripts_enforce_AccService_store_and_identity_gates()
    {
        var repoRoot = FindRepoRoot();
        var export = File.ReadAllText(Path.Combine(repoRoot, "SiOffice.AccService", "Export-AccAutodeskToken-ToShare.ps1"));
        var install = File.ReadAllText(Path.Combine(repoRoot, "SiOffice.AccService", "Install-AccAutodeskToken-FromShare.ps1"));
        var authOnce = File.ReadAllText(Path.Combine(repoRoot, "SiOffice.AccService.AuthOnce", "Program.cs"));

        Assert.Contains(
            "Join-Path $env:LOCALAPPDATA \"SiNet\\Autodesk\\AccService\\refresh_token.json\"",
            export,
            StringComparison.Ordinal);
        Assert.Contains("Test-IsGenericDesktopTokenPath", export, StringComparison.Ordinal);
        Assert.Contains("desktopForbidden", export, StringComparison.Ordinal);
        Assert.Contains("ActualAdminEmail", export, StringComparison.Ordinal);
        Assert.Contains("--verify", export, StringComparison.Ordinal);
        Assert.Contains("export_meta.txt", export, StringComparison.Ordinal);
        Assert.Contains("AccBootstrapAdminEmail", export, StringComparison.Ordinal);
        Assert.Contains("Get-AccBootstrapAdminEmailFromDb", export, StringComparison.Ordinal);
        Assert.Contains("Convert-ToSystemDataSqlClientConnectionString", export, StringComparison.Ordinal);
        Assert.Contains("Trust Server Certificate", export, StringComparison.Ordinal);
        Assert.Contains("TrustServerCertificate=", export, StringComparison.Ordinal);
        // Desktop path may appear only as a refusal check — never as the export source default.
        Assert.DoesNotContain(
            "SourceToken = $desktopForbidden",
            export,
            StringComparison.Ordinal);
        // Export must not hardcode the steady-state email as SoT (DB is canonical).
        Assert.DoesNotContain(
            "[string]$ExpectedAdminEmail = \"siad@si-eng.co.il\"",
            export,
            StringComparison.Ordinal);

        Assert.Contains("Autodesk\\AccService", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TokenPurpose", install, StringComparison.Ordinal);
        Assert.Contains("ActualAdminEmail", install, StringComparison.Ordinal);
        Assert.Contains("Resolve-ServiceAccount", install, StringComparison.Ordinal);
        Assert.Contains("desktopPath", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("admin-identity", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Remove-Item", install, StringComparison.Ordinal);
        Assert.Contains("Get-AccBootstrapAdminEmailFromDb", install, StringComparison.Ordinal);
        Assert.Contains("Convert-ToSystemDataSqlClientConnectionString", install, StringComparison.Ordinal);
        Assert.Contains("Trust Server Certificate", install, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[string]$ExpectedAdminEmail = \"siad@si-eng.co.il\"",
            install,
            StringComparison.Ordinal);
        // Must not leave live refresh tokens under used\.
        Assert.DoesNotContain(
            "refresh_token.{0}.json",
            install,
            StringComparison.Ordinal);

        Assert.Contains("--verify", authOnce, StringComparison.Ordinal);
        Assert.Contains("token_identity.txt", authOnce, StringComparison.Ordinal);
        Assert.Contains("AutodeskTokenStoreOptions.AccServiceAdmin", authOnce, StringComparison.Ordinal);
        Assert.Contains("ResolveExpectedAdminEmailFromDbAsync", authOnce, StringComparison.Ordinal);
        Assert.Contains("SystemSettingKeys.AccBootstrapAdminEmail", authOnce, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private const string DefaultExpectedAdminEmail = \"siad@si-eng.co.il\"",
            authOnce,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "Server=SI-WIN-2K19\\SIDATA;Database=SiData;Integrated Security=True;TrustServerCertificate=True;Encrypt=True",
        "TrustServerCertificate=True")]
    [InlineData(
        "Data Source=SI-WIN-2K19\\SIDATA;Initial Catalog=SiData;Integrated Security=True;Trust Server Certificate=True;Encrypt=True",
        "Trust Server Certificate=True")]
    public void SystemDataSqlClient_connection_string_normalizer_accepts_both_trust_keyword_forms(
        string input,
        string originalTrustFragment)
    {
        var normalized = ConvertToSystemDataSqlClientConnectionString(input);

        Assert.DoesNotContain("Trust Server Certificate", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrustServerCertificate=", normalized, StringComparison.OrdinalIgnoreCase);

        // Other keys unchanged (presence).
        Assert.Contains("Integrated Security=True", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Encrypt=True", normalized, StringComparison.OrdinalIgnoreCase);
        if (input.Contains("Server=", StringComparison.OrdinalIgnoreCase))
            Assert.Contains("Server=", normalized, StringComparison.OrdinalIgnoreCase);
        if (input.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            Assert.Contains("Data Source=", normalized, StringComparison.OrdinalIgnoreCase);
        if (input.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase))
            Assert.Contains("Initial Catalog=", normalized, StringComparison.OrdinalIgnoreCase);
        if (input.Contains("Database=", StringComparison.OrdinalIgnoreCase))
            Assert.Contains("Database=", normalized, StringComparison.OrdinalIgnoreCase);

        // Prove System.Data.SqlClient accepts the normalized string (ctor parses keywords).
        Assert.True(
            TryConstructSystemDataSqlClientConnection(normalized, out var error),
            $"SqlConnection rejected normalized string (from '{originalTrustFragment}'): {error}");
        Assert.False(
            TryConstructSystemDataSqlClientConnection(
                "Server=x;Database=y;Integrated Security=True;Trust Server Certificate=True",
                out _),
            "Spaced Trust Server Certificate must remain unsupported by System.Data.SqlClient");
    }

    [Fact]
    public void Export_and_Install_cmd_wrappers_use_unc_safe_pushd()
    {
        var repoRoot = FindRepoRoot();
        var exportCmd = File.ReadAllText(Path.Combine(repoRoot, "SiOffice.AccService", "Export-AccAutodeskToken-ToShare.cmd"));
        var installCmd = File.ReadAllText(Path.Combine(repoRoot, "SiOffice.AccService", "Install-AccAutodeskToken-FromShare.cmd"));
        var kitPublisher = File.ReadAllText(Path.Combine(repoRoot, "build", "publish-server-kit.ps1"));

        foreach (var text in new[] { exportCmd, installCmd })
        {
            Assert.Contains("pushd \"%~dp0\"", text, StringComparison.Ordinal);
            Assert.Contains("popd", text, StringComparison.Ordinal);
            Assert.DoesNotContain("cd /d \"%~dp0\"", text, StringComparison.Ordinal);
        }

        Assert.Contains("pushd \"\"%~dp0\"\"", kitPublisher, StringComparison.Ordinal);
        Assert.DoesNotContain("cd /d \"\"%~dp0\"\"", kitPublisher, StringComparison.Ordinal);
        Assert.Contains("AUTHONCE_PUBLISH_STAMP.txt", kitPublisher, StringComparison.Ordinal);
        Assert.Contains("Silent reuse of an older", kitPublisher, StringComparison.Ordinal);
        Assert.Contains("Copied fresh AuthOnce from", kitPublisher, StringComparison.Ordinal);
        Assert.DoesNotContain("WARNING: SiOffice.AccService.AuthOnce.exe not staged", kitPublisher, StringComparison.Ordinal);
        Assert.DoesNotContain("$authOnceCandidates", kitPublisher, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_all_builds_AuthOnce_before_Server_kit()
    {
        var publishAll = File.ReadAllText(Path.Combine(FindRepoRoot(), "publish-all.ps1"));
        Assert.Contains("SiOffice.AccService.AuthOnce\\publish-tool.ps1", publishAll, StringComparison.Ordinal);
        Assert.Contains("SINET_AUTHONCE_BUILD_SESSION", publishAll, StringComparison.Ordinal);
        Assert.Contains("SkipDeploy", publishAll, StringComparison.Ordinal);
        // AuthOnce block must appear before Server kit invocation.
        var authIdx = publishAll.IndexOf("SiOffice.AccService.AuthOnce\\publish-tool.ps1", StringComparison.Ordinal);
        var kitIdx = publishAll.IndexOf("build\\publish-server-kit.ps1", StringComparison.Ordinal);
        Assert.True(authIdx > 0 && kitIdx > authIdx, "AuthOnce publish must run before publish-server-kit");
    }

    [Fact]
    public void Export_script_does_not_AddContent_to_transcript_log()
    {
        var export = File.ReadAllText(Path.Combine(FindRepoRoot(), "SiOffice.AccService", "Export-AccAutodeskToken-ToShare.ps1"));
        Assert.Contains("Start-Transcript", export, StringComparison.Ordinal);
        Assert.DoesNotContain("Add-Content -Path $script:logFile", export, StringComparison.Ordinal);
        Assert.Contains("sharing violation", export, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mirrors <c>Convert-ToSystemDataSqlClientConnectionString</c> in the Acc token PS1 scripts.
    /// </summary>
    private static string ConvertToSystemDataSqlClientConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        return Regex.Replace(
            connectionString,
            @"(?i)(^|;)\s*Trust Server Certificate\s*=",
            "$1TrustServerCertificate=");
    }

    private static bool TryConstructSystemDataSqlClientConnection(string connectionString, out string? error)
    {
        error = null;
        // Windows PowerShell 5.1 hosts System.Data.SqlClient (same provider the ops scripts use).
        var ps = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments =
                "-NoProfile -NonInteractive -Command " +
                "\"$ErrorActionPreference='Stop'; " +
                "try { $null = New-Object System.Data.SqlClient.SqlConnection ([string]$env:SINET_CS); 'OK' } " +
                "catch { $_.Exception.Message; exit 2 }\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        ps.Environment["SINET_CS"] = connectionString;

        using var proc = System.Diagnostics.Process.Start(ps)
            ?? throw new InvalidOperationException("Failed to start powershell.exe");
        var stdout = proc.StandardOutput.ReadToEnd().Trim();
        var stderr = proc.StandardError.ReadToEnd().Trim();
        proc.WaitForExit(30_000);
        if (proc.ExitCode == 0 && stdout.Equals("OK", StringComparison.Ordinal))
            return true;

        error = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        return false;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
