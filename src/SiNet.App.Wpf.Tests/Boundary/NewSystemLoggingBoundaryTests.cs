using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SiNet.Application.Abstractions.Logging;
using SiNet.Infrastructure.Logging;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Stage 4 logging guards — see <c>docs/LOGGING.md</c> and <c>docs/NEW_SYSTEM_BOUNDARY.md</c>.
/// </summary>
public sealed class NewSystemLoggingBoundaryTests
{
    private static readonly string[] ForbiddenLegacyLoggingInAppWpf =
    [
        "SiNetSQL.Services.AppLogger",
        "using Serilog",
        "Log.Logger",
        "AppLogger.",
    ];

    [Fact]
    public void SerilogAppLogger_forwards_info_warn_error_to_serilog()
    {
        var sink = new CollectingSink();
        using var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        IAppLogger logger = new SerilogAppLogger(serilog);
        logger.Info("info-msg");
        logger.Warn("warn-msg");
        logger.Error("err-msg");
        logger.Error("err-ex", new InvalidOperationException("boom"));

        Assert.Equal(4, sink.Events.Count);
        Assert.Equal(LogEventLevel.Information, sink.Events[0].Level);
        Assert.Contains("info-msg", sink.Events[0].RenderMessage(), StringComparison.Ordinal);
        Assert.Equal(LogEventLevel.Warning, sink.Events[1].Level);
        Assert.Equal(LogEventLevel.Error, sink.Events[2].Level);
        Assert.Equal(LogEventLevel.Error, sink.Events[3].Level);
        Assert.NotNull(sink.Events[3].Exception);
    }

    [Fact]
    public void AddSiNetSerilogLogging_registers_SerilogAppLogger()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSiNetSerilogLogging();

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<IAppLogger>();

        Assert.IsType<SerilogAppLogger>(logger);
    }

    [Fact]
    public void AddSiNetLogging_registers_console_adapter_for_scaffold_only()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSiNetLogging();

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<IAppLogger>();

        Assert.IsType<ConsoleAppLogger>(logger);
    }

    [Fact]
    public void NewSystemServiceCollectionExtensions_registers_serilog_logging()
    {
        var source = File.ReadAllText(NewSystemExtensionsPath);
        Assert.Contains("AddSiNetSerilogLogging", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSiNetLogging()", source, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> AppWpfSourceFiles()
    {
        foreach (var file in EnumerateAppWpfSourceFiles())
        {
            yield return [Path.GetRelativePath(AppWpfRoot, file)];
        }
    }

    [Theory]
    [MemberData(nameof(AppWpfSourceFiles))]
    public void App_Wpf_source_does_not_reference_legacy_or_serilog_logging(string relativePath)
    {
        var content = File.ReadAllText(Path.Combine(AppWpfRoot, relativePath));

        foreach (var forbidden in ForbiddenLegacyLoggingInAppWpf)
        {
            Assert.False(
                content.Contains(forbidden, StringComparison.Ordinal),
                $"Forbidden logging reference '{forbidden}' in src/SiNet.App.Wpf/{relativePath}");
        }
    }

    [Fact]
    public void App_Wpf_csproj_does_not_reference_serilog_packages()
    {
        var content = File.ReadAllText(AppWpfCsprojPath);
        Assert.DoesNotContain("Serilog", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Logging_target_doc_exists_and_describes_port_and_adapter()
    {
        var doc = File.ReadAllText(LoggingDocPath);
        Assert.Contains("IAppLogger", doc, StringComparison.Ordinal);
        Assert.Contains("SerilogAppLogger", doc, StringComparison.Ordinal);
        Assert.Contains("AddSiNetSerilogLogging", doc, StringComparison.Ordinal);
        Assert.Contains("ConsoleAppLogger", doc, StringComparison.Ordinal);
    }

    private static string RepoRoot => RepoPaths.RepoRoot;

    private static string AppWpfRoot => Path.Combine(RepoRoot, "src", "SiNet.App.Wpf");

    private static string AppWpfCsprojPath => Path.Combine(AppWpfRoot, "SiNet.App.Wpf.csproj");

    private static string NewSystemExtensionsPath => Path.Combine(
        RepoRoot,
        "SiNetProjectManagerV2",
        "Services",
        "Composition",
        "NewSystemServiceCollectionExtensions.cs");

    private static string LoggingDocPath => Path.Combine(RepoRoot, "docs", "LOGGING.md");

    private static IEnumerable<string> EnumerateAppWpfSourceFiles()
    {
        if (!Directory.Exists(AppWpfRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(AppWpfRoot, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            yield return file;
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}

internal static class LogEventRendering
{
    internal static string RenderMessage(this LogEvent logEvent)
        => logEvent.RenderMessage(CultureInfo.InvariantCulture);
}
