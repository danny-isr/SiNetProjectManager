using System.IO;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Guards for docs/ACC_SERVICE_DECOUPLING.md slice B3 (DbContext + settings reads).</summary>
public sealed class AccServiceDecouplingB3BoundaryTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate repo root (SiNet.sln).");
        }
    }

    private static string AccServiceDir => Path.Combine(RepoRoot, "SiOffice.AccService");

    [Fact]
    public void AccService_csproj_references_Infrastructure_Sql()
    {
        var csproj = File.ReadAllText(Path.Combine(AccServiceDir, "SiOffice.AccService.csproj"));
        Assert.Contains("SiNet.Infrastructure.Sql", csproj, StringComparison.Ordinal);
        // B4 (see AccServiceDecouplingB4BoundaryTests) dropped the SiNetSQL.csproj reference entirely —
        // AccBootstrap/provisioning types now come from SiNet.Infrastructure.AccBootstrap.
        Assert.DoesNotContain("SiNetSQL.csproj", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_registers_AddSiNetSql_and_system_settings_sql()
    {
        var program = File.ReadAllText(Path.Combine(AccServiceDir, "Program.cs"));
        Assert.Contains("AddSiNetSql(", program, StringComparison.Ordinal);
        Assert.Contains("AddSiNetSystemSettingsSql(", program, StringComparison.Ordinal);
        Assert.Contains("AddSiNetAuthorizationSql(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddDbContextFactory<", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Inbox_ensure_uses_ISystemSettingsQueryService_not_legacy_SystemSettingsService()
    {
        var endpoints = File.ReadAllText(Path.Combine(AccServiceDir, "Endpoints", "AccEndpoints.cs"));
        Assert.Contains("ISystemSettingsQueryService", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SystemSettingsService settings",
            endpoints,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Program_no_longer_registers_legacy_SystemSettingsService()
    {
        // Superseded by B4: AccProjectProvisioningService now takes ISystemSettingsQueryService,
        // so the legacy SiNetSQL.Services.SystemSettingsService singleton was removed entirely.
        var program = File.ReadAllText(Path.Combine(AccServiceDir, "Program.cs"));
        Assert.DoesNotContain("AddSingleton<SystemSettingsService>", program, StringComparison.Ordinal);
    }
}
