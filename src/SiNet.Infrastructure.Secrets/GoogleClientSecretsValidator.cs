using System.Text.Json;

namespace SiNet.Infrastructure.Secrets;

/// <summary>Validates Google OAuth credentials.json content stored in the vault (not a file path).</summary>
public static class GoogleClientSecretsValidator
{
    public static (bool Success, string? Detail) ValidateJsonContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("installed", out var section) &&
                !root.TryGetProperty("web", out section))
            {
                return (false, "JSON לא מכיל 'installed' או 'web' — אינו credentials.json תקין");
            }

            if (!section.TryGetProperty("client_id", out _))
            {
                return (false, "חסר client_id ב-JSON");
            }

            if (!section.TryGetProperty("client_secret", out _))
            {
                return (false, "חסר client_secret ב-JSON");
            }

            return (true, "Google OAuth");
        }
        catch (Exception ex)
        {
            return (false, $"JSON לא תקין: {ex.Message}");
        }
    }
}
