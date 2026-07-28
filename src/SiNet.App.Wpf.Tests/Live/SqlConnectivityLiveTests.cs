using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Data;
using SiNet.Infrastructure.Sql;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

[Trait("Category", LiveFactAttribute.Category)]
public sealed class SqlConnectivityLiveTests
{
    [LiveFact]
    public async Task WhenLiveEnabledThenSchemaGateConnectsAndSchemaIsPresent()
    {
        var connectionString = LiveEnvironment.TryResolveSqlConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Fail(
                $"No SQL connection string. Set {LiveEnvironment.SqlConnectionEnv} or vault key {SecretCatalogSiNetDatabase}.");
        }

        var services = new ServiceCollection();
        services.AddSiNetSql(connectionString!);
        services.AddSiNetIdentitySql();
        await using var sp = services.BuildServiceProvider();

        var gate = sp.GetRequiredService<IDatabaseSchemaGate>();
        var result = await gate.ValidateAsync();

        Assert.True(result.CanConnect, "Cannot connect to SQL with the live connection string.");
        Assert.True(
            result.IsSchemaPresent,
            "Schema incomplete. Missing: " + string.Join(", ", result.MissingTables));
    }

    // Avoid pulling SecretCatalog into assert message via type if Secrets not imported — keep string.
    private const string SecretCatalogSiNetDatabase = "SiNet/ConnectionStrings/SiNetDatabase";
}
