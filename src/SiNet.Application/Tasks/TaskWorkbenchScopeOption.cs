namespace SiNet.Application.Tasks;

/// <summary>Scope option for Task Workbench scope selector binding.</summary>
public sealed record TaskWorkbenchScopeOption(TaskWorkbenchScope Scope, string DisplayName)
{
    public override string ToString() => DisplayName;
}
