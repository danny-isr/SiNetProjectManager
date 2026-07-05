namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Creates read-only Task Panel windows wired through DI (Application task ports only).
/// </summary>
public interface ITaskPanelReadOnlyWindowFactory
{
    TaskPanelReadOnlyView Create();
}
