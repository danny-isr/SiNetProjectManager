namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Centralized constants and tag builders for the native workflow engine.
/// Eliminates scattered inline string construction (e.g. "Stage:{id}").
/// Kept byte-compatible with the legacy <c>SiNetSQL.Services.Workflow.WorkflowConstants</c>
/// and <see cref="SiNet.Infrastructure.Sql.Services.Actions.WorkflowActionHelpers.BuildStageTag"/>
/// so stage-tagged <see cref="SiNetSQL.Models.TaskLink"/> rows resolve identically across engines.
/// </summary>
internal static class WorkflowConstants
{
    /// <summary>
    /// Builds the canonical tag stored in <see cref="SiNetSQL.Models.TaskLink.Description"/>
    /// to identify which stage a task belongs to.
    /// </summary>
    public static string BuildStageTag(int stageId) => $"Stage:{stageId}";
}
