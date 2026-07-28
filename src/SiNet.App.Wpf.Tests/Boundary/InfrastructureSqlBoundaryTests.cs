using System.IO;
using System.Xml.Linq;
using SiNet.Infrastructure.Sql.Services.Identity;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Ensures native New System SQL code does not reference the legacy SiNetSQL project namespaces.
/// </summary>
public sealed class InfrastructureSqlBoundaryTests
{
    private static readonly string[] ForbiddenInInfrastructureSqlSource =
    [
        "SiNetSQL.Data",
        "SiNetSQL.Models",
        "SiNetSQLDbContext",
        "Siuser",
    ];

    [Fact]
    public void Infrastructure_Sql_csproj_does_not_reference_SiNetSQL()
    {
        var references = ReadProjectReferences(InfrastructureSqlCsprojPath);
        Assert.DoesNotContain(
            references,
            r => r.Contains("SiNetSQL", StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<object[]> InfrastructureSqlSourceFiles()
    {
        foreach (var file in EnumerateInfrastructureSqlSourceFiles())
        {
            yield return [Path.GetRelativePath(InfrastructureSqlRoot, file)];
        }
    }

    [Theory]
    [MemberData(nameof(InfrastructureSqlSourceFiles))]
    public void Infrastructure_Sql_source_does_not_contain_legacy_SiNetSQL_identifiers(string relativePath)
    {
        // Legacy monolith context/models remain under SiNetSQL.* namespaces until migrated slice-by-slice.
        // Native user-admin code must use SiNetDbContext + SiNet.Infrastructure.Sql.Entities only.
        if (!relativePath.StartsWith("Services\\Identity", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Services/Identity", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Services\\MasterPlan", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Services/MasterPlan", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Entities", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Data\\SiNetDbContext", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Data/SiNetDbContext", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Data\\Configurations\\UserManagement", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Data/Configurations/UserManagement", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Data\\Configurations\\ActionPermissionEntity", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith("Data/Configurations/ActionPermissionEntity", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Known debt outside the standalone-host slice: user-group services still query via
        // SiNetSQLDbContext (namespace SiNetSQL.Data) until a dedicated SiNetDbContext projection exists.
        var fileName = Path.GetFileName(relativePath);
        if (fileName is "SqlUserGroupQueryService.cs" or "SqlUserGroupCommandService.cs")
        {
            return;
        }

        var content = File.ReadAllText(Path.Combine(InfrastructureSqlRoot, relativePath));
        foreach (var forbidden in ForbiddenInInfrastructureSqlSource)
        {
            Assert.False(
                content.Contains(forbidden, StringComparison.Ordinal),
                $"Forbidden legacy identifier '{forbidden}' in src/SiNet.Infrastructure.Sql/{relativePath}");
        }
    }

    [Fact]
    public void SqlUserManagementService_uses_SiNetDbContext_and_native_entities_only()
    {
        var source = File.ReadAllText(SqlUserManagementServicePath);
        Assert.Contains("SiNetDbContext", source, StringComparison.Ordinal);
        Assert.Contains("SiUserEntity", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.Data", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.Models", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQLDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Siuser", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlActionPermissionAdminService_uses_SiNetDbContext_and_native_entities_only()
    {
        var path = Path.Combine(InfrastructureSqlRoot, "Services", "Identity", "SqlActionPermissionAdminService.cs");
        var source = File.ReadAllText(path);
        Assert.Contains("SiNetDbContext", source, StringComparison.Ordinal);
        Assert.Contains("ActionPermissionEntity", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.Data", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.Models", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQLDbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionPermissionService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlMasterPlanEmployeeLookupService_uses_native_sql_only()
    {
        var path = Path.Combine(InfrastructureSqlRoot, "Services", "MasterPlan", "SqlMasterPlanEmployeeLookupService.cs");
        var source = File.ReadAllText(path);
        Assert.Contains("Microsoft.Data.SqlClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.Data", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.Models", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SiNetSQL.MVVM", source, StringComparison.Ordinal);
    }

    private static string RepoRoot => RepoPaths.RepoRoot;

    private static string InfrastructureSqlRoot => Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql");

    private static string InfrastructureSqlCsprojPath => Path.Combine(InfrastructureSqlRoot, "SiNet.Infrastructure.Sql.csproj");

    private static string SqlUserManagementServicePath =>
        Path.Combine(InfrastructureSqlRoot, "Services", "Identity", "SqlUserManagementService.cs");

    private static IEnumerable<string> EnumerateInfrastructureSqlSourceFiles()
    {
        if (!Directory.Exists(InfrastructureSqlRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(InfrastructureSqlRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            yield return file;
        }
    }

    private static IReadOnlyList<string> ReadProjectReferences(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        return doc
            .Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToList();
    }
}
