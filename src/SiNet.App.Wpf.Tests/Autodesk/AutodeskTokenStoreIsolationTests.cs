using System.IO;
using MyOffice.AutodeskConnector;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using Xunit;

namespace SiNet.App.Wpf.Tests.Autodesk;

public sealed class AutodeskTokenStoreIsolationTests
{
    [Fact]
    public void Desktop_and_AccService_default_paths_differ()
    {
        var desktop = AutodeskTokenStorePaths.GetDefaultRefreshTokenFilePath(AutodeskTokenStorePurpose.UserContext);
        var service = AutodeskTokenStorePaths.GetDefaultRefreshTokenFilePath(AutodeskTokenStorePurpose.AccServiceAdmin);

        Assert.NotEqual(desktop, service, StringComparer.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("SiNet", "Autodesk", "refresh_token.json"),
            desktop,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("SiNet", "Autodesk", "AccService", "refresh_token.json"),
            service,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TokenProvider_default_ctor_uses_UserContext_store()
    {
        var provider = new TokenProvider("client-id-xxxx", "secret");
        Assert.Equal(AutodeskTokenStorePurpose.UserContext, provider.TokenStorePurpose);
        Assert.Equal(
            AutodeskTokenStorePaths.GetDefaultRefreshTokenFilePath(AutodeskTokenStorePurpose.UserContext),
            provider.ThreeLeggedRefreshTokenStoragePath,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TokenProvider_AccServiceAdmin_uses_dedicated_store()
    {
        var provider = new TokenProvider("client-id-xxxx", "secret", AutodeskTokenStoreOptions.AccServiceAdmin);
        Assert.Equal(AutodeskTokenStorePurpose.AccServiceAdmin, provider.TokenStorePurpose);
        Assert.Equal(
            AutodeskTokenStorePaths.GetDefaultRefreshTokenFilePath(AutodeskTokenStorePurpose.AccServiceAdmin),
            provider.ThreeLeggedRefreshTokenStoragePath,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Writing_desktop_store_does_not_change_AccService_store()
    {
        var root = Path.Combine(Path.GetTempPath(), "SiNetTokenIsolation-" + Guid.NewGuid().ToString("N"));
        var desktopDir = Path.Combine(root, "desktop");
        var serviceDir = Path.Combine(root, "service");
        Directory.CreateDirectory(desktopDir);
        Directory.CreateDirectory(serviceDir);

        try
        {
            var desktopPath = Path.Combine(desktopDir, AutodeskTokenStorePaths.RefreshTokenFileName);
            var servicePath = Path.Combine(serviceDir, AutodeskTokenStorePaths.RefreshTokenFileName);
            File.WriteAllText(servicePath, "{\"marker\":\"acc-service-original\"}");

            File.WriteAllText(desktopPath, "{\"marker\":\"desktop-updated\"}");

            Assert.Equal("{\"marker\":\"acc-service-original\"}", File.ReadAllText(servicePath));
            Assert.Equal("{\"marker\":\"desktop-updated\"}", File.ReadAllText(desktopPath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Writing_AccService_store_does_not_change_desktop_store()
    {
        var root = Path.Combine(Path.GetTempPath(), "SiNetTokenIsolation-" + Guid.NewGuid().ToString("N"));
        var desktopDir = Path.Combine(root, "desktop");
        var serviceDir = Path.Combine(root, "service");
        Directory.CreateDirectory(desktopDir);
        Directory.CreateDirectory(serviceDir);

        try
        {
            var desktopPath = Path.Combine(desktopDir, AutodeskTokenStorePaths.RefreshTokenFileName);
            var servicePath = Path.Combine(serviceDir, AutodeskTokenStorePaths.RefreshTokenFileName);
            File.WriteAllText(desktopPath, "{\"marker\":\"desktop-original\"}");

            File.WriteAllText(servicePath, "{\"marker\":\"acc-service-updated\"}");

            Assert.Equal("{\"marker\":\"desktop-original\"}", File.ReadAllText(desktopPath));
            Assert.Equal("{\"marker\":\"acc-service-updated\"}", File.ReadAllText(servicePath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AccService_TokenMissing_when_only_desktop_store_has_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "SiNetTokenIsolation-" + Guid.NewGuid().ToString("N"));
        var desktopDir = Path.Combine(root, "desktop");
        var serviceDir = Path.Combine(root, "service");
        Directory.CreateDirectory(desktopDir);
        Directory.CreateDirectory(serviceDir);

        try
        {
            File.WriteAllText(
                Path.Combine(desktopDir, AutodeskTokenStorePaths.RefreshTokenFileName),
                "{\"refresh_token\":\"desktop-only-placeholder\"}");

            var serviceProvider = new TokenProvider(
                "client-id-xxxx",
                "secret",
                new AutodeskTokenStoreOptions
                {
                    Purpose = AutodeskTokenStorePurpose.AccServiceAdmin,
                    TokenDirectory = serviceDir,
                });

            Assert.False(serviceProvider.HasThreeLeggedRefreshToken);
            Assert.False(File.Exists(serviceProvider.ThreeLeggedRefreshTokenStoragePath));

            var check = AccServiceAdminIdentity.Evaluate(
                SystemSettingsDefaults.AccBootstrapAdminEmail,
                actualAdminEmail: null,
                tokenAvailable: serviceProvider.HasThreeLeggedRefreshToken,
                profileResolved: false);

            Assert.Equal(AccServiceAdminIdentityStatus.TokenMissing, check.Status);
            Assert.True(AccServiceAdminIdentity.ShouldBlockAdminMutation(check));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Expected_siad_with_service_token_danny_is_AdminEmailMismatch()
    {
        var check = AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "danny@si-eng.co.il");
        Assert.Equal(AccServiceAdminIdentityStatus.AdminEmailMismatch, check.Status);
    }

    [Fact]
    public void Expected_siad_with_service_token_siad_is_Healthy()
    {
        var check = AccServiceAdminIdentity.Evaluate("siad@si-eng.co.il", "siad@si-eng.co.il");
        Assert.Equal(AccServiceAdminIdentityStatus.Healthy, check.Status);
        Assert.True(check.EmailMatch);
    }

    [Fact]
    public void AccService_ops_scripts_and_AuthOnce_target_same_AccService_store()
    {
        var expectedRelative = Path.Combine("SiNet", "Autodesk", "AccService", "refresh_token.json");
        var canonical = AutodeskTokenStorePaths.GetDefaultRefreshTokenFilePath(AutodeskTokenStorePurpose.AccServiceAdmin);
        Assert.EndsWith(expectedRelative, canonical, StringComparison.OrdinalIgnoreCase);

        var repoRoot = FindRepoRoot();
        var sources = new[]
        {
            Path.Combine(repoRoot, "SiOffice.AccService", "Export-AccAutodeskToken-ToShare.ps1"),
            Path.Combine(repoRoot, "SiOffice.AccService", "Install-AccAutodeskToken-FromShare.ps1"),
            Path.Combine(repoRoot, "SiOffice.AccService", "Refresh-AccService-Token.ps1"),
            Path.Combine(repoRoot, "SiOffice.AccService.AuthOnce", "Program.cs"),
        };

        foreach (var path in sources)
        {
            Assert.True(File.Exists(path), $"Missing: {path}");
            var text = File.ReadAllText(path);
            Assert.Contains("AccService", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "Join-Path $env:LOCALAPPDATA \"SiNet\\Autodesk\\refresh_token.json\"",
                text,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "Autodesk\\AccService",
            File.ReadAllText(Path.Combine(repoRoot, "SiOffice.AccService", "Export-AccAutodeskToken-ToShare.ps1")),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Autodesk\\AccService",
            File.ReadAllText(Path.Combine(repoRoot, "SiOffice.AccService", "Install-AccAutodeskToken-FromShare.ps1")),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Autodesk\\AccService",
            File.ReadAllText(Path.Combine(repoRoot, "SiOffice.AccService", "Refresh-AccService-Token.ps1")),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "AutodeskTokenStoreOptions.AccServiceAdmin",
            File.ReadAllText(Path.Combine(repoRoot, "SiOffice.AccService.AuthOnce", "Program.cs")),
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                || File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test BaseDirectory.");
    }
}
