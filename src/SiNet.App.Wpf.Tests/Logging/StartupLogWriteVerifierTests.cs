using System.IO;
using Serilog;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Logging;
using Xunit;

namespace SiNet.App.Wpf.Tests.Logging;

/// <summary>
/// DEV-028: Warning logged but missing from the dated file must fail verify (temp dirs, not prod UNC).
/// </summary>
[Collection(StandaloneLoggingCollection.Name)]
public sealed class StartupLogWriteVerifierTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "SiNetLogWriteVerify",
        Guid.NewGuid().ToString("N"));

    public StartupLogWriteVerifierTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        StandaloneHostLoggingBootstrap.TestCentralLogPathOverride = null;
        Log.CloseAndFlush();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void WhenVerifyRunsThenMarkerIsInLocalAndCentralTempFiles()
    {
        var localDir = Path.Combine(_root, "local");
        var centralRoot = Path.Combine(_root, "central");
        Directory.CreateDirectory(localDir);
        Directory.CreateDirectory(centralRoot);

        StandaloneHostLoggingBootstrap.TestCentralLogPathOverride = centralRoot;
        StandaloneHostLoggingBootstrap.ConfigureDefault();
        StandaloneHostLoggingBootstrap.ApplyUserLogging(new UserLoggingSettingsDto(
            LoggingEnabled: true,
            LogDirectory: localDir,
            BootstrapDefaultLocalLogDirectory: localDir,
            AppLoggerDefaultLocalLogDirectory: localDir));

        // Rebuild again so central override is attached after directory apply.
        StandaloneHostLoggingBootstrap.FlushPipeline();

        Assert.True(CentralLoggingBuilder.CentralSinkEnabled);

        var result = StartupLogWriteVerifier.Verify(TimeSpan.FromSeconds(10));

        Assert.True(result.LocalOk, $"local fail: {result.Detail} path={result.LocalPath}");
        Assert.True(result.CentralConfigured);
        Assert.True(result.CentralOk, $"central fail: {result.Detail} path={result.CentralPath}");
        Assert.Contains($"pid={Environment.ProcessId}", result.Marker, StringComparison.Ordinal);
        Assert.Contains("מקומי + מרכזי", result.SplashStatusHe, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenCentralIsSyncFileThenMarkerVisibleWithoutCloseAndFlush()
    {
        var localDir = Path.Combine(_root, "local2");
        var centralRoot = Path.Combine(_root, "central2");
        Directory.CreateDirectory(localDir);
        Directory.CreateDirectory(centralRoot);

        var config = new CentralLoggingConfig
        {
            App = SiNetApp.Client,
            CentralLogPath = centralRoot,
            LocalLogDirectory = localDir,
            CentralMinLevel = Serilog.Events.LogEventLevel.Warning,
            LocalFileLevelSwitch = new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Debug),
        };

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .AddSiNetCentralLogging(config)
            .CreateLogger();

        var marker = $"[STARTUP] Client process alive pid={Environment.ProcessId}-sync";
        Log.Warning(marker);
        // No CloseAndFlush — sync central File must already have bytes (DEV-028 Slice D).

        var userDir = Path.Combine(centralRoot, "Client", Environment.MachineName, Environment.UserName);
        Assert.True(Directory.Exists(userDir), $"missing {userDir}");
        var centralText = ReadAll(userDir);
        Assert.Contains(marker, centralText, StringComparison.Ordinal);
    }

    [Fact]
    public void FileContainsMarker_returns_false_when_file_missing()
    {
        Assert.False(
            StartupLogWriteVerifier.FileContainsMarker(
                Path.Combine(_root, "nope.log"),
                "marker",
                CancellationToken.None));
    }

    private static string ReadAll(string directory)
    {
        if (!Directory.Exists(directory))
            return string.Empty;

        return string.Concat(
            Directory.EnumerateFiles(directory, "*.log", SearchOption.AllDirectories)
                .Select(path =>
                {
                    using var stream = new FileStream(
                        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }));
    }
}
