using Microsoft.Data.SqlClient;

namespace SiNet.Infrastructure.Secrets;

/// <summary>Normalizes user-pasted SQL connection strings before vault storage.</summary>
public static class ConnectionStringNormalizer
{
    public static string Normalize(string raw)
    {
        var trimmed = raw.Trim();
        try
        {
            var csb = new SqlConnectionStringBuilder(trimmed);
            if (csb.DataSource.Contains("\\\\"))
            {
                csb.DataSource = csb.DataSource.Replace("\\\\", "\\");
            }

            if (!csb.TrustServerCertificate)
            {
                csb.TrustServerCertificate = true;
            }

            return csb.ConnectionString;
        }
        catch
        {
            return trimmed;
        }
    }
}
