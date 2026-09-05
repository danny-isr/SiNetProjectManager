using System.IO;
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
