using System.IO;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Infrastructure;
using Xunit;

namespace SiNet.App.Wpf.Tests.Infrastructure;

public sealed class ErrorHandlingSafetyNetTests
{
    [Fact]
    public void App_xaml_cs_configures_global_exception_handling()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/App.xaml.cs");
        Assert.Contains("AppGlobalExceptionHandling.Configure", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppGlobalExceptionHandling_wires_all_three_handlers()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Infrastructure/AppGlobalExceptionHandling.cs");
        Assert.Contains("DispatcherUnhandledException", source, StringComparison.Ordinal);
        Assert.Contains("UnhandledException", source, StringComparison.Ordinal);
        Assert.Contains("UnobservedTaskException", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsyncRelayCommand_execute_catches_exception_without_rethrowing()
    {
        Exception? reported = null;
        void Handler(Exception ex, string _) => reported = ex;
        AppErrorReporter.ExceptionReported += Handler;
        try
        {
            var command = new AsyncRelayCommand(() => throw new InvalidOperationException("boom"));
            command.Execute(null);
            await Task.Delay(50);
            Assert.NotNull(reported);
            Assert.IsType<InvalidOperationException>(reported);
        }
        finally
        {
            AppErrorReporter.ExceptionReported -= Handler;
        }
    }

    [Fact]
    public void InspectionTreeViewModel_load_methods_have_catch_blocks()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Inspection/InspectionTreeViewModel.cs");
        Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
        Assert.Contains(nameof(AppErrorReporter.Report), source, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellFactory_action_permissions_and_inspection_have_try_catch()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Shell/NewShellFactory.cs");
        Assert.Contains("private void OpenNativeActionPermissions()", source, StringComparison.Ordinal);
        Assert.Contains("private void OpenInspectionShell()", source, StringComparison.Ordinal);

        var actionPermissionsBody = ExtractMethodBody(source, "OpenNativeActionPermissions");
        var inspectionBody = ExtractMethodBody(source, "OpenInspectionShell");

        Assert.Contains("try", actionPermissionsBody, StringComparison.Ordinal);
        Assert.Contains("catch (Exception", actionPermissionsBody, StringComparison.Ordinal);
        Assert.Contains("try", inspectionBody, StringComparison.Ordinal);
        Assert.Contains("catch (Exception", inspectionBody, StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        var marker = $"void {methodName}(";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method {methodName} not found");
        var braceStart = source.IndexOf('{', start);
        var depth = 0;
        for (var i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[braceStart..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract body for {methodName}");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
