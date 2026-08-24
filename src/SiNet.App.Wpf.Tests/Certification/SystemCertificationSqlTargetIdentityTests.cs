using Xunit;

namespace SiNet.App.Wpf.Tests.Certification;

public sealed class SystemCertificationSqlTargetIdentityTests
{
    [Fact]
    public void When_declared_endpoint_is_allowlisted_but_actual_server_is_not_then_authorization_fails()
    {
        var violation = SystemCertificationSqlTargetIdentity.EvaluateAllowlist(
            actualServerName: "danny\\SQLEXPRESS",
            actualDatabaseName: "SiData",
            allowedServers: ["SI-WIN-2K19\\SIDATA"],
            allowedDatabases: ["SiData"]);

        Assert.NotNull(violation);
        Assert.Contains("Actual SQL server 'danny\\SQLEXPRESS'", violation, StringComparison.Ordinal);
        Assert.Contains(SystemCertificationEnvironment.AllowedServersEnv, violation, StringComparison.Ordinal);
        Assert.Contains("declared endpoint", violation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void When_actual_server_and_database_are_allowlisted_then_authorization_passes()
    {
        var violation = SystemCertificationSqlTargetIdentity.EvaluateAllowlist(
            actualServerName: "danny\\SQLEXPRESS",
            actualDatabaseName: "SiData",
            allowedServers: ["danny\\SQLEXPRESS"],
            allowedDatabases: ["SiData"]);

        Assert.Null(violation);
    }

    [Fact]
    public void When_actual_database_is_not_allowlisted_then_authorization_fails()
    {
        var violation = SystemCertificationSqlTargetIdentity.EvaluateAllowlist(
            actualServerName: "danny\\SQLEXPRESS",
            actualDatabaseName: "SiData",
            allowedServers: ["danny\\SQLEXPRESS"],
            allowedDatabases: ["ProductionDb"]);

        Assert.NotNull(violation);
        Assert.Contains("Actual database 'SiData'", violation, StringComparison.Ordinal);
    }
}
