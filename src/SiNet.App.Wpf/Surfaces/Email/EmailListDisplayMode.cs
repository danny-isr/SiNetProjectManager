namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>How the standalone email list loads and pages messages.</summary>
public enum EmailListDisplayMode
{
    /// <summary>General inbox — Gmail pageToken stack, 50 items per page.</summary>
    AllEmails,

    /// <summary>Selected project — Gmail project label query, 10 items per "show more" chunk.</summary>
    ProjectEmails,
}
