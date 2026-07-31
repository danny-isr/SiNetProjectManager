using SiNet.App.Wpf.Inspection;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Admin.ProjectTypeWorkflowPolicy;

public sealed class ProjectTypeWorkflowPolicyMappingRowVm : ObservableObject
{
    public ProjectTypeWorkflowPolicyMappingRowVm(ProjectTypeWorkflowMappingDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        Id = dto.Id;
        ProjectTypeId = dto.ProjectTypeId;
        ProjectTypeTitle = dto.ProjectTypeTitle;
        WorkflowDefinitionId = dto.WorkflowDefinitionId;
        WorkflowDefinitionCode = dto.WorkflowDefinitionCode;
        WorkflowDefinitionName = dto.WorkflowDefinitionName;
        IsDefault = dto.IsDefault;
        IsEnabled = dto.IsEnabled;
        SortOrder = dto.SortOrder;
    }

    public int Id { get; }
    public int ProjectTypeId { get; }
    public string ProjectTypeTitle { get; }
    public int WorkflowDefinitionId { get; }
    public string WorkflowDefinitionCode { get; }
    public string WorkflowDefinitionName { get; }
    public bool IsDefault { get; }
    public bool IsEnabled { get; }
    public int SortOrder { get; }

    public string DisplayWorkflow =>
        string.IsNullOrWhiteSpace(WorkflowDefinitionCode)
            ? WorkflowDefinitionName
            : $"{WorkflowDefinitionCode} — {WorkflowDefinitionName}";

    public string DefaultLabel => IsDefault ? "ברירת מחדל" : string.Empty;
    public string EnabledLabel => IsEnabled ? "פעיל" : "כבוי";
}
