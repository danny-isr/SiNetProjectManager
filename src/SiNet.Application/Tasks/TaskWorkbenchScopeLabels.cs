namespace SiNet.Application.Tasks;

/// <summary>Display labels for <see cref="TaskWorkbenchScope"/> in the Task Workbench UI.</summary>
public static class TaskWorkbenchScopeLabels
{
    public const string MyTasks = "המשימות שלי";
    public const string SpecificUser = "משתמש מסוים";
    public const string AllUsers = "כל המשתמשים";

    public static string GetDisplayName(TaskWorkbenchScope scope) =>
        scope switch
        {
            TaskWorkbenchScope.MyTasks => MyTasks,
            TaskWorkbenchScope.SpecificUser => SpecificUser,
            TaskWorkbenchScope.AllUsers => AllUsers,
            _ => scope.ToString(),
        };
}
