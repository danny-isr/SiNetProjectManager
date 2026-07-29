using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Logging;
using Xunit;

namespace SiNet.App.Wpf.Tests.Logging;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StandaloneLoggingCollection
{
    public const string Name = "StandaloneLogging";
}

/// <summary>
/// Behavioral guards for <c>docs/LOGGING.md</c> §9: the per-user toggle must move the local level
/// switch only, and must never silence the central network sink.
/// Mutates the global Serilog logger, so this collection runs without parallelization.
/// </summary>
[Collection(StandaloneLoggingCollection.Name)]
public sealed class StandaloneHostLoggingBootstrapTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "SiNetLoggingTests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Log.CloseAndFlush();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Rolling file handles may linger; temp cleanup is best-effort.
        }
    }

    [Fact]
    public void WhenUserTogglesLoggingOffThenCentralSinkKeepsWritingAndLocalStops()
    {
        var localDir = Path.Combine(_root, "local");
        var centralRoot = Path.Combine(_root, "central");
        Directory.CreateDirectory(localDir);
        Directory.CreateDirectory(centralRoot);

        var localSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);
        var config = new CentralLoggingConfig
        {
            App = SiNetApp.Client,
            CentralLogPath = centralRoot,
            LocalLogDirectory = localDir,
            CentralMinLevel = LogEventLevel.Warning,
            LocalFileLevelSwitch = localSwitch,
        };

        using (var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .AddSiNetCentralLogging(config)
            .CreateLogger())
        {
            logger.Warning("MARKER-BEFORE-TOGGLE");

            // User switches "detailed logging" off — this must affect the local sink only.
            localSwitch.MinimumLevel = LogEventLevel.Fatal;

            logger.Warning("MARKER-AFTER-TOGGLE");
        }

        var localText = ReadAllLogs(localDir);
        var centralText = ReadAllLogs(centralRoot);

        Assert.Contains("MARKER-BEFORE-TOGGLE", localText, StringComparison.Ordinal);
        Assert.DoesNotContain("MARKER-AFTER-TOGGLE", localText, StringComparison.Ordinal);

        Assert.Contains("MARKER-BEFORE-TOGGLE", centralText, StringComparison.Ordinal);
        Assert.Contains("MARKER-AFTER-TOGGLE", centralText, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenOnlyTheToggleChangesThenApplyUserLoggingDoesNotRebuildThePipeline()
    {
        var directory = Path.Combine(_root, "no-rebuild");

        StandaloneHostLoggingBootstrap.ApplyUserLogging(Settings(enabled: true, directory));
        var before = Log.Logger;

        StandaloneHostLoggingBootstrap.ApplyUserLogging(Settings(enabled: false, directory));

        Assert.Same(before, Log.Logger);
    }

    [Fact]
    public void WhenLogDirectoryChangesThenApplyUserLoggingRebuildsThePipeline()
    {
        StandaloneHostLoggingBootstrap.ApplyUserLogging(Settings(true, Path.Combine(_root, "dir-a")));
        var before = Log.Logger;

        StandaloneHostLoggingBootstrap.ApplyUserLogging(Settings(true, Path.Combine(_root, "dir-b")));

        Assert.NotSame(before, Log.Logger);
    }

    [Fact]
    public void WhenToggleIsOffThenLocalFileStopsReceivingInformation()
    {
        var directory = Path.Combine(_root, "toggle");

        StandaloneHostLoggingBootstrap.ApplyUserLogging(Settings(enabled: true, directory));
        StandaloneHostLoggingBootstrap.Info("MARKER-ENABLED");

        StandaloneHostLoggingBootstrap.ApplyUserLogging(Settings(enabled: false, directory));
        StandaloneHostLoggingBootstrap.Info("MARKER-DISABLED");

        Log.CloseAndFlush();
        var text = ReadAllLogs(directory);

        Assert.Contains("MARKER-ENABLED", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MARKER-DISABLED", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenToggleIsReenabledThenLocalFileReceivesAgain()
    {
        var directory = Path.Combine(_root, "reenable");

        StandaloneHostLoggingBootstrap.ApplyUserLogging(Settings(enabled: false, directory));
        StandaloneHostLoggingBootstrap.Info("MARKER-OFF");

        StandaloneHostLoggingBootstrap.ApplyUserLogging(Settings(enabled: true, directory));
        StandaloneHostLoggingBootstrap.Info("MARKER-ON");

        Log.CloseAndFlush();
        var text = ReadAllLogs(directory);

        Assert.DoesNotContain("MARKER-OFF", text, StringComparison.Ordinal);
        Assert.Contains("MARKER-ON", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fails if the bootstrap ever goes back to building a bare <c>WriteTo.File</c> pipeline:
    /// only <c>AddSiNetCentralLogging</c> populates these diagnostics.
    /// </summary>
    [Fact]
    public void StandaloneBootstrapBuildsThroughTheSharedCentralSinkLayout()
    {
        var directory = Path.Combine(_root, "layout");

        StandaloneHostLoggingBootstrap.ApplyUserLogging(Settings(enabled: true, directory));

        Assert.Equal(directory, CentralLoggingBuilder.LocalSinkTargetDirectory);
        Assert.EndsWith(
            $"{SiNetApp.Client}-.log",
            CentralLoggingBuilder.LocalSinkTargetFile!,
            StringComparison.Ordinal);
    }

    private static UserLoggingSettingsDto Settings(bool enabled, string directory) =>
        new(enabled, directory, string.Empty, string.Empty);

    private static string ReadAllLogs(string root)
    {
        if (!Directory.Exists(root))
        {
            return string.Empty;
        }

        var contents = Directory
            .EnumerateFiles(root, "*.log", SearchOption.AllDirectories)
            .Select(ReadShared);

        return string.Join(Environment.NewLine, contents);
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
