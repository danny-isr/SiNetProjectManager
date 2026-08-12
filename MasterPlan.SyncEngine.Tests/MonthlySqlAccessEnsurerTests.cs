using MasterPlan.SyncEngine;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class MonthlySqlAccessEnsurerTests
{
    [Fact]
    public void QuoteBracket_escapes_closing_bracket()
    {
        Assert.Equal("[a]]b]", MonthlySqlAccessEnsurer.QuoteBracket("a]b"));
    }

    [Fact]
    public void QuoteBracket_wraps_hebrew_windows_principal()
    {
        Assert.Equal(@"[SI-ENG\שרטטים]", MonthlySqlAccessEnsurer.QuoteBracket(@"SI-ENG\שרטטים"));
    }

    [Fact]
    public void BuildEnsureLoginSql_uses_windows_login_and_existence_check()
    {
        var sql = MonthlySqlAccessEnsurer.BuildEnsureLoginSql(@"SI-ENG\שרטטים");
        Assert.Contains(@"N'SI-ENG\שרטטים'", sql, StringComparison.Ordinal);
        Assert.Contains(@"CREATE LOGIN [SI-ENG\שרטטים] FROM WINDOWS", sql, StringComparison.Ordinal);
        Assert.Contains("sys.server_principals", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEnsureUserAndRolesSql_creates_user_and_both_write_roles()
    {
        var sql = MonthlySqlAccessEnsurer.BuildEnsureUserAndRolesSql(
            @"SI-ENG\שרטטים",
            ["db_datareader", "db_datawriter"]);

        Assert.Contains(@"CREATE USER [SI-ENG\שרטטים] FOR LOGIN [SI-ENG\שרטטים]", sql, StringComparison.Ordinal);
        Assert.Contains(@"ALTER USER [SI-ENG\שרטטים] WITH LOGIN = [SI-ENG\שרטטים]", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER ROLE [db_datareader] ADD MEMBER", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER ROLE [db_datawriter] ADD MEMBER", sql, StringComparison.Ordinal);
        Assert.Contains("IS_ROLEMEMBER", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEnsureUserAndRolesSql_rejects_unsafe_role_name()
    {
        Assert.Throws<ArgumentException>(() =>
            MonthlySqlAccessEnsurer.BuildEnsureUserAndRolesSql(@"SI-ENG\x", ["db_datareader; DROP"]));
    }

    [Fact]
    public void Default_options_include_draftsmen_group_and_write_roles()
    {
        var options = new MonthlySqlAccessOptions();
        Assert.True(options.Enabled);
        Assert.Contains(MonthlySqlAccessOptions.DefaultPrincipal, options.WindowsPrincipals);
        Assert.Contains("db_datawriter", options.DatabaseRoles);
        Assert.Contains("Db_Mp_SiEng", options.Databases);
        Assert.Contains("Replica_DB", options.Databases);
    }

    [Fact]
    public void FromConfiguration_reads_principals_and_can_disable()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MonthlySqlAccess:Enabled"] = "false",
                ["MonthlySqlAccess:WindowsPrincipals:0"] = @"SI-ENG\Other",
                ["MonthlySqlAccess:DatabaseRoles:0"] = "db_datareader",
                ["MonthlySqlAccess:Databases:0"] = "Db_Mp_SiEng",
            })
            .Build();

        var options = MonthlySqlAccessOptions.FromConfiguration(config);
        Assert.False(options.Enabled);
        Assert.Equal(@"SI-ENG\Other", Assert.Single(options.WindowsPrincipals));
        Assert.Equal("db_datareader", Assert.Single(options.DatabaseRoles));
        Assert.Equal("Db_Mp_SiEng", Assert.Single(options.Databases));
    }
}
