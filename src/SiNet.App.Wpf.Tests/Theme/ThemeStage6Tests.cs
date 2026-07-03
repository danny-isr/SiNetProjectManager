using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Logging;
using Xunit;

namespace SiNet.App.Wpf.Tests.Theme;

public sealed class ThemeStage6Tests
{
    [Fact]
    public void Typography_defaults_are_within_validation_ranges()
    {
        Assert.True(TypographyThemeDefaults.TryValidateScales(
            TypographyThemeDefaults.TextTinyScale,
            TypographyThemeDefaults.TextSmallScale,
            TypographyThemeDefaults.TextNormalScale,
            TypographyThemeDefaults.TextMediumScale,
            TypographyThemeDefaults.TextLargeScale,
            TypographyThemeDefaults.TextHugeScale,
            out var error),
            error);
    }

    [Fact]
    public void Typography_validation_rejects_out_of_range_tiny_scale()
    {
        Assert.False(TypographyThemeDefaults.TryValidateScales(
            0.50,
            TypographyThemeDefaults.TextSmallScale,
            TypographyThemeDefaults.TextNormalScale,
            TypographyThemeDefaults.TextMediumScale,
            TypographyThemeDefaults.TextLargeScale,
            TypographyThemeDefaults.TextHugeScale,
            out _));
    }

    [Fact]
    public void Theme_calculator_computes_font_sizes_from_base_and_scales()
    {
        var appearance = TypographyThemeDefaults.CreateDefaultAppearance() with { BaseFontSize = 10 };
        var computed = ThemeCalculator.Compute(appearance);

        Assert.Equal(8.0, computed.TextTinyFontSize);
        Assert.Equal(10.0, computed.TextNormalFontSize);
        Assert.Equal(18.0, computed.TextHugeFontSize);
    }

    [Fact]
    public void JsonAppSettingsService_round_trips_all_theme_fields_and_preserves_unknown()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sinet-theme-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");

        try
        {
            File.WriteAllText(settingsPath, """
                {
                  "FontFamily": "Tahoma",
                  "FontSize": 14,
                  "BaseFontSize": 14,
                  "TextTinyScale": 0.75,
                  "TextSmallScale": 0.85,
                  "TextNormalScale": 1.0,
                  "TextMediumScale": 1.15,
                  "TextLargeScale": 1.40,
                  "TextHugeScale": 1.70,
                  "ForegroundColor": "#111111",
                  "BackgroundColor": "#FAFAFA",
                  "PrimaryColor": "#123456",
                  "SecondaryColor": "#654321",
                  "customThemeFutureField": "keep-me"
                }
                """);

            var dto = JsonAppSettingsService.ReadDto(settingsPath);
            Assert.Equal("Tahoma", dto.Appearance.FontFamily);
            Assert.Equal(14, dto.Appearance.BaseFontSize);
            Assert.Equal(0.75, dto.Appearance.TextTinyScale);
            Assert.Equal("#123456", dto.Appearance.PrimaryColor);

            JsonAppSettingsService.WriteDto(settingsPath, dto);
            var merged = File.ReadAllText(settingsPath);
            Assert.Contains("\"customThemeFutureField\": \"keep-me\"", merged, StringComparison.Ordinal);
            Assert.Contains("\"PrimaryColor\": \"#123456\"", merged, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Personal_save_with_appearance_change_applies_theme_runtime_applier()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appSettings = MockAppSettings(saveCompleted);
        var themeRuntime = new Mock<IThemeRuntimeApplier>();

        var vm = CreatePersonalViewModel(appSettings, themeRuntime: themeRuntime);
        await vm.LoadAsync();
        vm.PrimaryColor = "#ABCDEF";
        vm.SaveCommand.Execute(null);
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        themeRuntime.Verify(t => t.ApplyUserAppearance(It.Is<UserAppearanceSettingsDto>(a => a.PrimaryColor == "#ABCDEF")), Times.Once);
    }

    [Fact]
    public async Task Personal_save_without_appearance_change_does_not_apply_theme_runtime_applier()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appSettings = MockAppSettings(saveCompleted);
        var themeRuntime = new Mock<IThemeRuntimeApplier>();

        var vm = CreatePersonalViewModel(appSettings, themeRuntime: themeRuntime);
        await vm.LoadAsync();
        vm.SaveCommand.Execute(null);
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        themeRuntime.Verify(t => t.ApplyUserAppearance(It.IsAny<UserAppearanceSettingsDto>()), Times.Never);
    }

    [Fact]
    public async Task Personal_save_appearance_change_does_not_apply_logging_runtime_applier()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appSettings = MockAppSettings(saveCompleted);
        var loggingRuntime = new Mock<ILoggingRuntimeApplier>();
        var themeRuntime = new Mock<IThemeRuntimeApplier>();

        var vm = CreatePersonalViewModel(appSettings, loggingRuntime: loggingRuntime, themeRuntime: themeRuntime);
        await vm.LoadAsync();
        vm.BaseFontSize = 16;
        vm.SaveCommand.Execute(null);
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        loggingRuntime.Verify(l => l.ApplyUserLogging(It.IsAny<UserLoggingSettingsDto>()), Times.Never);
        themeRuntime.Verify(t => t.ApplyUserAppearance(It.IsAny<UserAppearanceSettingsDto>()), Times.Once);
    }

    [Fact]
    public void ThemeResourceLoader_is_invoked_from_theme_startup_and_runtime_applier()
    {
        var loaderSource = File.ReadAllText(Path.Combine(AppWpfRoot, "Theme", "ThemeResourceLoader.cs"));
        var applierSource = File.ReadAllText(Path.Combine(AppWpfRoot, "Theme", "WpfThemeRuntimeApplier.cs"));
        Assert.Contains("EnsureApplicationResourcesMerged", loaderSource, StringComparison.Ordinal);
        Assert.Contains("EnsureApplicationResourcesMerged", applierSource, StringComparison.Ordinal);
        Assert.Contains("Theme/TypographyResources.xaml", loaderSource, StringComparison.Ordinal);
        Assert.Contains("ThemeStyles.xaml", loaderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void NewShellFactory_ensures_theme_resources_before_creating_shell()
    {
        var source = File.ReadAllText(Path.Combine(AppWpfRoot, "Shell", "NewShellFactory.cs"));
        Assert.Contains("ThemeResourceLoader.EnsureApplicationResourcesMerged", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_xaml_files_define_required_resource_keys()
    {
        var themeDir = Path.Combine(RepoRoot, "src", "SiNet.App.Wpf", "Theme");
        var typography = File.ReadAllText(Path.Combine(themeDir, "TypographyResources.xaml"));
        var brushes = File.ReadAllText(Path.Combine(themeDir, "BrushResources.xaml"));
        var styles = File.ReadAllText(Path.Combine(themeDir, "ThemeStyles.xaml"));

        foreach (var key in ThemeResourceKeys.AllFontSizeKeys.Append(ThemeResourceKeys.FontFamily))
        {
            Assert.Contains($"x:Key=\"{key}\"", typography, StringComparison.Ordinal);
        }

        foreach (var key in ThemeResourceKeys.AllBrushKeys)
        {
            Assert.Contains($"x:Key=\"{key}\"", brushes, StringComparison.Ordinal);
        }

        Assert.Contains($"x:Key=\"{ThemeResourceKeys.TextNormalStyle}\"", styles, StringComparison.Ordinal);
        Assert.Contains($"x:Key=\"{ThemeResourceKeys.PrimaryButtonStyle}\"", styles, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Shell/NewShellWindow.xaml")]
    [InlineData("Shared/Projects/ProjectSelectorView.xaml")]
    [InlineData("Admin/Users/UserManagementView.xaml")]
    [InlineData("Admin/Users/AddUserView.xaml")]
    [InlineData("Admin/Permissions/ActionPermissionsView.xaml")]
    [InlineData("Admin/Security/SecretSetupView.xaml")]
    [InlineData("Admin/Settings/SettingsView.xaml")]
    [InlineData("Inspection/InspectionShellView.xaml")]
    [InlineData("Surfaces/Email/EmailWindowView.xaml")]
    [InlineData("Surfaces/Inspection/InspectionWindowView.xaml")]
    public void Migrated_native_xaml_does_not_use_hardcoded_font_size_literals(string relativePath)
    {
        var content = File.ReadAllText(Path.Combine(AppWpfRoot, relativePath));
        var matches = Regex.Matches(content, @"FontSize\s*=\s*""[0-9]");
        Assert.True(matches.Count == 0,
            $"Hardcoded FontSize literals found in {relativePath}: {string.Join(", ", matches.Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value))}");
    }

    private static SettingsViewModel CreatePersonalViewModel(
        Mock<IAppSettingsService> appSettings,
        Mock<ILoggingRuntimeApplier>? loggingRuntime = null,
        Mock<IThemeRuntimeApplier>? themeRuntime = null)
    {
        loggingRuntime ??= new Mock<ILoggingRuntimeApplier>();
        themeRuntime ??= new Mock<IThemeRuntimeApplier>();

        var auth = new Mock<IAuthorizationQueryService>();
        auth.Setup(a => a.CanCurrentUserAccessFeatureAsync(
                AppFeatureCodes.SystemSettingsWrite,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return new SettingsViewModel(
            appSettings.Object,
            new Mock<ISystemSettingsQueryService>().Object,
            new Mock<ISystemSettingsCommandService>().Object,
            new Mock<ILoggingSettingsCommandService>().Object,
            loggingRuntime.Object,
            themeRuntime.Object,
            new Mock<IStatusColorSettingsService>().Object,
            auth.Object,
            new StubCurrentUser(1),
            SettingsSurfaceScope.Personal);
    }

    private static Mock<IAppSettingsService> MockAppSettings(TaskCompletionSource onSave)
    {
        var mock = new Mock<IAppSettingsService>();
        mock.Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonAppSettingsService.CreateDefaultDto());
        mock.Setup(s => s.SaveUserAppSettingsAsync(It.IsAny<UserAppSettingsDto>(), It.IsAny<CancellationToken>()))
            .Returns<UserAppSettingsDto, CancellationToken>((_, _) =>
            {
                onSave.TrySetResult();
                return Task.CompletedTask;
            });
        return mock;
    }

    private static string RepoRoot => Boundary.RepoPaths.RepoRoot;
    private static string AppWpfRoot => Path.Combine(RepoRoot, "src", "SiNet.App.Wpf");

    private sealed class StubCurrentUser(int userId) : ICurrentUserContext
    {
        public int? UserId { get; } = userId;
    }
}
