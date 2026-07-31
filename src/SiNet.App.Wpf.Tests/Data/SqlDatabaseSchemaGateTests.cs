using SiNet.Infrastructure.Sql.Services.Data;
using Xunit;

namespace SiNet.App.Wpf.Tests.Data;

public sealed class SqlDatabaseSchemaGateTests
{
    [Fact]
    public void Evaluate_when_cannot_connect_then_not_ready()
    {
        var result = SqlDatabaseSchemaGate.Evaluate(
            canConnect: false,
            missingTables: ["TaskType"],
            pendingMigrations: []);

        Assert.False(result.CanConnect);
        Assert.False(result.IsSchemaPresent);
        Assert.False(result.HasPendingMigrations);
        Assert.False(result.IsReady);
    }

    [Fact]
    public void Evaluate_when_tables_missing_then_not_ready()
    {
        var result = SqlDatabaseSchemaGate.Evaluate(
            canConnect: true,
            missingTables: ["TaskType", "UserSetting"],
            pendingMigrations: []);

        Assert.True(result.CanConnect);
        Assert.False(result.IsSchemaPresent);
        Assert.Equal(2, result.MissingTables.Count);
        Assert.False(result.IsReady);
    }

    [Fact]
    public void Evaluate_when_pending_migrations_then_not_ready()
    {
        var result = SqlDatabaseSchemaGate.Evaluate(
            canConnect: true,
            missingTables: [],
            pendingMigrations: ["20260726190048_AddProjectFileIsRequiredAndCode"]);

        Assert.True(result.CanConnect);
        Assert.True(result.IsSchemaPresent);
        Assert.True(result.HasPendingMigrations);
        Assert.False(result.IsReady);
        Assert.Contains(
            "20260726190048_AddProjectFileIsRequiredAndCode",
            result.PendingMigrations,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Evaluate_when_tables_present_and_no_pending_then_ready()
    {
        var result = SqlDatabaseSchemaGate.Evaluate(
            canConnect: true,
            missingTables: [],
            pendingMigrations: []);

        Assert.True(result.IsReady);
        Assert.False(result.HasPendingMigrations);
    }
}
