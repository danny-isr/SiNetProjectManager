namespace SiNet.Application.Actions;

/// <summary>Well-known keys for <see cref="ActionExecutionCommand.Data"/>.</summary>
public static class ActionExecutionDataKeys
{
    public const string ProjectStatusCode = "ProjectStatusCode";
    public const string TaskResultCode = "TaskResultCode";
    public const string FromStageId = "FromStageId";
    public const string ToStageId = "ToStageId";
    public const string ConfigJson = "ConfigJson";
}
