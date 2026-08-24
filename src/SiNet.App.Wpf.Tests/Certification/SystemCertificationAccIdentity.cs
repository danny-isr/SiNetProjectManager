namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// ACC identity normalization for certification read-backs. Production stores project ids in SQL without
/// the BIM360 <c>b.</c> prefix while browse responses may include it.
/// </summary>
internal static class SystemCertificationAccIdentity
{
    internal static string NormalizeProjectId(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return string.Empty;
        }

        var trimmed = projectId.Trim();
        return trimmed.StartsWith("b.", StringComparison.OrdinalIgnoreCase)
            ? trimmed[2..]
            : trimmed;
    }

    internal static bool ProjectIdsMatch(string? left, string? right) =>
        string.Equals(
            NormalizeProjectId(left),
            NormalizeProjectId(right),
            StringComparison.OrdinalIgnoreCase);
}
