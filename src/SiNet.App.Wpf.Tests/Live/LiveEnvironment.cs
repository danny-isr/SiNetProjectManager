using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Secrets;
using SiOffice.AccService.Contracts;

namespace SiNet.App.Wpf.Tests.Live;

internal static class LiveEnvironment
{
    public const string SqlConnectionEnv = "SINET_LIVE_SQL_CONNECTION";
    public const string AccBaseUrlEnv = "SINET_LIVE_ACC_BASEURL";

    public static string? TryResolveSqlConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable(SqlConnectionEnv);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        try
        {
            var services = new ServiceCollection();
            services.AddSiNetSecrets();
            using var sp = services.BuildServiceProvider();
            var vault = sp.GetRequiredService<ISecretVaultStore>();
            var raw = vault.GetSecret(SecretCatalog.SiNetDatabase);
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static string AccBaseUrl =>
        (Environment.GetEnvironmentVariable(AccBaseUrlEnv) ?? "https://localhost:8443").TrimEnd('/');

    public static string? TryResolveAccApiKey()
    {
        try
        {
            var services = new ServiceCollection();
            services.AddSiNetSecrets();
            using var sp = services.BuildServiceProvider();
            var vault = sp.GetRequiredService<ISecretVaultStore>();
            var raw = vault.GetSecret(SecretCatalog.AccServiceApiKey);
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static ISecretVaultStore CreateVault()
    {
        var services = new ServiceCollection();
        services.AddSiNetSecrets();
        return services.BuildServiceProvider().GetRequiredService<ISecretVaultStore>();
    }

    public static string HealthUrl => $"{AccBaseUrl}{AccServiceContracts.ApiVersionPrefix}/acc/health";

    public static string DiagUrl => $"{AccBaseUrl}{AccServiceContracts.ApiVersionPrefix}/acc/diag";
}
