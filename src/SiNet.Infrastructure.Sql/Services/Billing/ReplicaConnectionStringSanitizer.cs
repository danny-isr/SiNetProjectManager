using Microsoft.Data.SqlClient;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>
/// Extracts Replica DataSource / InitialCatalog without exposing password or the raw connection string.
/// </summary>
public static class ReplicaConnectionStringSanitizer
{
    public static (string? DataSource, string? InitialCatalog) ReadIdentity(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return (null, null);

        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            Password = string.Empty
        };

        return (NullIfWhiteSpace(builder.DataSource), NullIfWhiteSpace(builder.InitialCatalog));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
