namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Specifies which ACC platform API to use for project creation.
/// 
/// This is the SINGLE SOURCE OF TRUTH for the platform selection enum.
/// All projects should reference this enum from SiNetSQL.Services.AccBootstrap namespace.
/// </summary>
public enum CreateProjectPlatform
{
    /// <summary>
    /// Use new ACC Admin API (recommended for new projects).
    /// Requires 3-legged OAuth and proper app approval in ACC Admin Console.
    /// </summary>
    AccNative,

    /// <summary>
    /// Use legacy BIM 360 HQ API (for backward compatibility).
    /// Works with 2-legged OAuth but creates legacy-style projects.
    /// </summary>
    LegacyBim360
}
