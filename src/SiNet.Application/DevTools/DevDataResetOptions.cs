namespace SiNet.Application.DevTools;

/// <summary>
/// Options for a development-database reset. Destructive — DEBUG hosts only.
/// </summary>
public sealed class DevDataResetOptions
{
    /// <summary>When true (default), <c>SystemSettings</c> is not wiped.</summary>
    public bool PreserveSystemSettings { get; init; } = true;

    /// <summary>When false (default), user/group settings tables are preserved.</summary>
    public bool ResetUserSettings { get; init; } = false;

    /// <summary>Re-run task static lookup seed after wipe.</summary>
    public bool IncludeTaskSeed { get; init; } = true;

    /// <summary>Re-run ProjectType mapping seed after wipe.</summary>
    public bool IncludeMappingsSeed { get; init; } = true;

    /// <summary>Re-run workflow / user-group seed after wipe.</summary>
    public bool IncludeWorkflowSeed { get; init; } = true;

    /// <summary>Seed demo tasks for Task Panel after reset/seed.</summary>
    public bool IncludeDemoTasks { get; init; } = false;
}
