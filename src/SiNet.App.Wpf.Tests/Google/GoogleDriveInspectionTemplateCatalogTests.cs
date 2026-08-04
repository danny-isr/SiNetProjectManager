using System.IO;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Google;
using Xunit;

namespace SiNet.App.Wpf.Tests.Google;

public sealed class GoogleDriveInspectionTemplateCatalogTests
{
    [Fact]
    public async Task ListTemplatesAsync_returns_empty_when_folder_id_missing()
    {
        var catalog = new GoogleDriveInspectionTemplateCatalog(
            CreateProvider(),
            new StubSettings(new InspectionSystemSettingsDto(string.Empty, "", "", "")),
            new StubLogger());

        var items = await catalog.ListTemplatesAsync();

        Assert.Empty(items);
    }

    [Fact]
    public async Task ListTemplatesAsync_returns_empty_when_drive_unavailable()
    {
        var catalog = new GoogleDriveInspectionTemplateCatalog(
            CreateProvider(),
            new StubSettings(new InspectionSystemSettingsDto("folder-1", "", "", "")),
            new StubLogger());

        // Provider without secrets / token yields null DriveService — catalog must not throw.
        var items = await catalog.ListTemplatesAsync();

        Assert.Empty(items);
    }

    private static GmailClientProvider CreateProvider()
        => new(
            new GmailOptions
            {
                ClientSecretsPath = string.Empty,
                TokenStorePath = Path.Combine(Path.GetTempPath(), "sinet-test-tokens"),
                ApplicationName = "SiNet.Tests",
                AllowInteractiveSignIn = false,
            },
            new StubLogger());

    private sealed class StubSettings(InspectionSystemSettingsDto inspection) : ISystemSettingsQueryService
    {
        public Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default)
        {
            var emptyLevel = new AiModelLevelSelectionDto(string.Empty, string.Empty);
            return Task.FromResult(new SystemSettingsDto(
                new EmailOfficeSystemSettingsDto("", "", "", "", null, 0),
                new AccSystemSettingsDto("", "", "", "", ""),
                inspection,
                new InspectionStatusLabelsDto("", "", "", ""),
                new AiSystemSettingsDto("", "", emptyLevel, emptyLevel, emptyLevel, emptyLevel, ""),
                new CentralLoggingSettingsDto(
                    null,
                    7,
                    7,
                    new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
                    new AppLogLevelsDto(LogLevelDto.Warning, LogLevelDto.Error),
                    new AppLogLevelsDto(LogLevelDto.Error, LogLevelDto.Error),
                    false),
                new WorkflowSystemSettingsDto(2),
                new ProjectWorkSystemSettingsDto(SystemSettingsDefaults.ProjectWorkScanExclusionRules)));
        }
    }

    private sealed class StubLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
