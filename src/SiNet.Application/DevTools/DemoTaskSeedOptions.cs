namespace SiNet.Application.DevTools;

/// <summary>
/// Target identity for demo task seed. Prefer passing <see cref="TargetUserId"/> from
/// <see cref="Identity.ICurrentUserContext"/> so Task Panel and demo seed align.
/// </summary>
public sealed class DemoTaskSeedOptions
{
    /// <summary>Application user id to assign demo tasks to.</summary>
    public int? TargetUserId { get; init; }

    /// <summary>Optional project id; when null the demo project is used or created.</summary>
    public int? TargetProjectId { get; init; }

    /// <summary>
    /// When true (default), seed fails if <see cref="TargetUserId"/> is missing instead of
    /// picking an arbitrary active user.
    /// </summary>
    public bool RequireCurrentUser { get; init; } = true;

    /// <summary>
    /// When true, prefer <see cref="TargetProjectId"/> from current project context when set.
    /// </summary>
    public bool UseCurrentProject { get; init; } = true;
}
