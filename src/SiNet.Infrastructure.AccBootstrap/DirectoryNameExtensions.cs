namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Native port of the <c>FixDirectoryName</c> helper from the legacy
/// <c>SiNetSQL.MyExtensions.DataFunc</c> static class, scoped to this project so
/// <see cref="AccProjectProvisioningService"/> does not need a ProjectReference to SiNetSQL
/// (see docs/ACC_SERVICE_DECOUPLING.md, slice B4). Behavior is unchanged from the original.
/// </summary>
internal static class DirectoryNameExtensions
{
    public static string? FixDirectoryName(this string nameVal)
    {
        if (string.IsNullOrEmpty(nameVal))
            return null;
        return nameVal.Trim()
                      .Replace("    ", " ")
                      .Replace("   ", " ")
                      .Replace("  ", " ")
                      .RemovUnElodChrInDirectoryName()
                      .Replace(" ", "_");
    }

    private static string? RemovUnElodChrInDirectoryName(this string nameVal)
    {
        if (string.IsNullOrEmpty(nameVal))
            return null;
        return nameVal.Trim()
                      .Replace("\\", "")
                      .Replace("/", "")
                      .Replace("\"", "")
                      .Replace(":", "")
                      .Replace("*", "")
                      .Replace("?", "")
                      .Replace("<", "")
                      .Replace(">", "")
                      .Replace("|", "");
    }
}
