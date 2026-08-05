using SiNet.Application.ProjectWork;
using SiNet.Application.Settings;
using SiNet.Domain.Files;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class ProjectWorkScanExclusionsTests
{
    [Theory]
    [InlineData("drawing.bak")]
    [InlineData("drawing.dwg.bak")]
    [InlineData("template.dwt")]
    [InlineData("lock.dwl")]
    [InlineData("lock.dwl2")]
    [InlineData("desktop.ini")]
    [InlineData("scratch.$ds")]
    [InlineData("fail.err")]
    [InlineData("temp.tmp")]
    [InlineData("trace.log")]
    [InlineData("tool.exe")]
    [InlineData("~$quote.docx")]
    public void Default_matches_legacy_noise_and_office_lock(string fileName)
        => Assert.True(ProjectWorkScanExclusions.IsExcludedExtension(fileName));

    [Fact]
    public void Default_does_not_match_normal_project_file()
        => Assert.False(ProjectWorkScanExclusions.IsExcludedExtension("quote.pdf"));

    [Fact]
    public void Parse_empty_falls_back_to_default()
    {
        var parsed = ProjectWorkScanExclusions.Parse("  ");
        Assert.True(parsed.Matches("x.bak"));
        Assert.True(parsed.Matches("~$lock.docx"));
    }

    [Fact]
    public void Parse_custom_extension_and_prefix()
    {
        var parsed = ProjectWorkScanExclusions.Parse(".xyz,LOCK_");
        Assert.True(parsed.Matches("a.xyz"));
        Assert.True(parsed.Matches("LOCK_note.txt"));
        Assert.False(parsed.Matches("a.bak"));
        Assert.False(parsed.Matches("~$quote.docx"));
    }

    [Fact]
    public void Removing_tilde_prefix_from_rules_stops_excluding_office_locks()
    {
        var withoutLock = ProjectWorkScanExclusions.Parse(".bak,.log");
        Assert.False(withoutLock.Matches("~$quote.docx"));
        Assert.True(withoutLock.Matches("old.bak"));
    }

    [Fact]
    public void Settings_default_matches_domain_default_csv()
        => Assert.Equal(
            ProjectWorkScanExclusions.DefaultRulesCsv,
            SystemSettingsDefaults.ProjectWorkScanExclusionRules);

    [Fact]
    public async Task Policy_ReplaceRules_updates_ShouldExclude()
    {
        var policy = new SettingsBackedProjectWorkScanExclusionPolicy();
        Assert.True(policy.ShouldExclude("x.bak"));

        policy.ReplaceRules(".xyz");
        Assert.False(policy.ShouldExclude("x.bak"));
        Assert.True(policy.ShouldExclude("x.xyz"));

        await policy.RefreshAsync();
        Assert.True(policy.ShouldExclude("x.bak"));
    }

    [Fact]
    public async Task Policy_RefreshAsync_reads_settings_dto()
    {
        var settings = new StubSettings(
            new ProjectWorkSystemSettingsDto(".custom,~TMP"));
        var policy = new SettingsBackedProjectWorkScanExclusionPolicy(settings);

        await policy.RefreshAsync();

        Assert.True(policy.ShouldExclude("a.custom"));
        Assert.True(policy.ShouldExclude("~TMPfile.txt"));
        Assert.False(policy.ShouldExclude("a.bak"));
    }

    private sealed class StubSettings(ProjectWorkSystemSettingsDto projectWork) : ISystemSettingsQueryService
    {
        public Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default)
        {
            var emptyLevel = new AiModelLevelSelectionDto(string.Empty, string.Empty);
            return Task.FromResult(new SystemSettingsDto(
                new EmailOfficeSystemSettingsDto("", "", "", "", null, 0),
                new AccSystemSettingsDto("", "", "", "", ""),
                new InspectionSystemSettingsDto("", "", "", ""),
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
                projectWork,
                SystemSettingsDefaults.Diagnostics));
        }
    }
}
