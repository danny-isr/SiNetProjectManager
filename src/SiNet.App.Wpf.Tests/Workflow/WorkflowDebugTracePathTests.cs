using System.IO;
using SiNet.Application.Diagnostics;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

public sealed class WorkflowDebugTracePathTests
{
    [Fact]
    public void FilePath_ends_with_workflow_manual_debug_log_under_Logs()
    {
        var path = WorkflowDebugTrace.FilePath;
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.EndsWith("workflow-manual-debug.log", path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{Path.DirectorySeparatorChar}Logs{Path.DirectorySeparatorChar}", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FilePath_is_under_local_application_data()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = WorkflowDebugTrace.FilePath;
        Assert.StartsWith(root, path, StringComparison.OrdinalIgnoreCase);
    }
}
