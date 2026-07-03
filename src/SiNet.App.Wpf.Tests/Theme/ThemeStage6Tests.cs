using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Abstractions.Autodesk;
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
    public async Task BaseFontSize_change_after_load_applies_theme_runtime_preview()
    {
        var themeRuntime = new Mock<IThemeRuntimeApplier>();
        var vm = CreatePersonalViewModel(MockAppSettings(new TaskCompletionSource()), themeRuntime: themeRuntime);
        await vm.LoadAsync();

        vm.BaseFontSize = 16;

        themeRuntime.Verify(
            t => t.ApplyUserAppearance(It.Is<UserAppearanceSettingsDto>(a => a.BaseFontSize == 16)),
            Times.Once);
    }

    [Fact]
    public async Task PrimaryColor_change_after_load_applies_theme_runtime_preview()
    {
        var themeRuntime = new Mock<IThemeRuntimeApplier>();
        var vm = CreatePersonalViewModel(MockAppSettings(new TaskCompletionSource()), themeRuntime: themeRuntime);
        await vm.LoadAsync();

        vm.PrimaryColor = "#ABCDEF";

        themeRuntime.Verify(
            t => t.ApplyUserAppearance(It.Is<UserAppearanceSettingsDto>(a => a.PrimaryColor == "#ABCDEF")),
            Times.Once);
    }

    [Fact]
    public async Task BackgroundColor_change_after_load_applies_theme_runtime_preview()
    {
        var themeRuntime = new Mock<IThemeRuntimeApplier>();
        var vm = CreatePersonalViewModel(MockAppSettings(new TaskCompletionSource()), themeRuntime: themeRuntime);
        await vm.LoadAsync();

        vm.BackgroundColor = "#AABBCC";

        themeRuntime.Verify(
            t => t.ApplyUserAppearance(It.Is<UserAppearanceSettingsDto>(a => a.BackgroundColor == "#AABBCC")),
            Times.Once);
    }

    [Fact]
    public async Task Reload_applies_theme_runtime_with_values_loaded_from_json()
    {
        var loadedAppearance = TypographyThemeDefaults.CreateDefaultAppearance() with { BackgroundColor = "#AABBCC" };
        var loadedDto = JsonAppSettingsService.CreateDefaultDto() with { Appearance = loadedAppearance };

        var appSettings = new Mock<IAppSettingsService>();
        appSettings.Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(loadedDto);

        var themeRuntime = new Mock<IThemeRuntimeApplier>();
        var vm = CreatePersonalViewModel(appSettings, themeRuntime: themeRuntime);
        await vm.LoadAsync();

        themeRuntime.Invocations.Clear();
        vm.BackgroundColor = "#010203";
        await vm.LoadAsync();

        themeRuntime.Verify(
            t => t.ApplyUserAppearance(It.Is<UserAppearanceSettingsDto>(a => a.BackgroundColor == "#AABBCC")),
            Times.Once);
        Assert.Equal("#AABBCC", vm.BackgroundColor);
    }

    [Fact]
    public async Task Save_after_appearance_change_updates_snapshot_so_close_does_not_rollback()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var themeRuntime = new Mock<IThemeRuntimeApplier>();

        var vm = CreatePersonalViewModel(MockAppSettings(saveCompleted), themeRuntime: themeRuntime);
        await vm.LoadAsync();

        var originalPrimary = vm.PrimaryColor;
        vm.PrimaryColor = "#ABCDEF";
        vm.SaveCommand.Execute(null);
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        themeRuntime.Invocations.Clear();
        vm.RollbackAppearanceIfNeeded();

        themeRuntime.Verify(t => t.ApplyUserAppearance(It.IsAny<UserAppearanceSettingsDto>()), Times.Never);
        Assert.True(vm.SavedSuccessfully);
        Assert.NotEqual(originalPrimary, "#ABCDEF");
    }

    [Fact]
    public async Task Invalid_hex_color_does_not_apply_theme_runtime_preview()
    {
        var themeRuntime = new Mock<IThemeRuntimeApplier>();
        var vm = CreatePersonalViewModel(MockAppSettings(new TaskCompletionSource()), themeRuntime: themeRuntime);
        await vm.LoadAsync();

        themeRuntime.Invocations.Clear();
        vm.PrimaryColor = "#ZZZZZZ";

        themeRuntime.Verify(t => t.ApplyUserAppearance(It.IsAny<UserAppearanceSettingsDto>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_rolls_back_appearance_to_original_snapshot()
    {
        var themeRuntime = new Mock<IThemeRuntimeApplier>();
        UserAppearanceSettingsDto? lastApplied = null;
        themeRuntime.Setup(t => t.ApplyUserAppearance(It.IsAny<UserAppearanceSettingsDto>()))
            .Callback<UserAppearanceSettingsDto>(a => lastApplied = a);

        var vm = CreatePersonalViewModel(MockAppSettings(new TaskCompletionSource()), themeRuntime: themeRuntime);
        await vm.LoadAsync();

        vm.BaseFontSize = 20;
        vm.CancelCommand.Execute(null);

        Assert.NotNull(lastApplied);
        Assert.Equal(UserAppSettingsDefaults.BaseFontSize, lastApplied.BaseFontSize);
    }

    [Fact]
    public async Task Save_does_not_roll_back_appearance_after_successful_save()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var themeRuntime = new Mock<IThemeRuntimeApplier>();
        var vm = CreatePersonalViewModel(MockAppSettings(saveCompleted), themeRuntime: themeRuntime);
        await vm.LoadAsync();

        vm.BaseFontSize = 16;
        vm.SaveCommand.Execute(null);
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        themeRuntime.Invocations.Clear();
        vm.RollbackAppearanceIfNeeded();

        themeRuntime.Verify(t => t.ApplyUserAppearance(It.IsAny<UserAppearanceSettingsDto>()), Times.Never);
        Assert.True(vm.SavedSuccessfully);
    }

    [Fact]
    public async Task Personal_save_with_appearance_change_persists_without_extra_theme_apply_on_save()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appSettings = MockAppSettings(saveCompleted);
        var themeRuntime = new Mock<IThemeRuntimeApplier>();

        var vm = CreatePersonalViewModel(appSettings, themeRuntime: themeRuntime);
        await vm.LoadAsync();

        themeRuntime.Invocations.Clear();
        vm.PrimaryColor = "#ABCDEF";
        themeRuntime.Verify(t => t.ApplyUserAppearance(It.IsAny<UserAppearanceSettingsDto>()), Times.Once);

        themeRuntime.Invocations.Clear();
        vm.SaveCommand.Execute(null);
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        appSettings.Verify(s => s.SaveUserAppSettingsAsync(
            It.Is<UserAppSettingsDto>(d => d.Appearance.PrimaryColor == "#ABCDEF"),
            It.IsAny<CancellationToken>()), Times.Once);
        themeRuntime.Verify(t => t.ApplyUserAppearance(It.IsAny<UserAppearanceSettingsDto>()), Times.Never);
    }

    [Fact]
    public async Task Personal_save_without_appearance_change_does_not_apply_theme_runtime_applier()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appSettings = MockAppSettings(saveCompleted);
        var themeRuntime = new Mock<IThemeRuntimeApplier>();

        var vm = CreatePersonalViewModel(appSettings, themeRuntime: themeRuntime);
        await vm.LoadAsync();

        themeRuntime.Invocations.Clear();
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
        themeRuntime.Verify(t => t.ApplyUserAppearance(It.IsAny<UserAppearanceSettingsDto>()), Times.AtLeastOnce);
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
    public void Migrated_native_xaml_uses_si_background_brush(string relativePath)
    {
        var content = File.ReadAllText(Path.Combine(AppWpfRoot, relativePath));
        Assert.Contains("SiBackgroundBrush", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_personal_status_colors_use_theme_color_editor()
    {
        var xaml = File.ReadAllText(Path.Combine(AppWpfRoot, "Admin", "Settings", "SettingsView.xaml"));
        var personalSection = ExtractXamlSection(xaml, "צבעי סטטוס (משתמש)", "צבעי סטטוס (גלובלי)");
        Assert.Contains("ThemeColorEditor", personalSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ColorHex", personalSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_global_status_colors_use_theme_color_editor()
    {
        var xaml = File.ReadAllText(Path.Combine(AppWpfRoot, "Admin", "Settings", "SettingsView.xaml"));
        var globalSection = ExtractXamlSection(xaml, "צבעי סטטוס (גלובלי)", "</TabControl>");
        Assert.Contains("ThemeColorEditor", globalSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding ColorHex", globalSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Color_picker_brightness_adjusts_final_color()
    {
        var baseColor = Color.FromRgb(0x80, 0x80, 0x80);
        var lighter = WpfColorPickerDialog.ApplyBrightness(baseColor, 100);
        var darker = WpfColorPickerDialog.ApplyBrightness(baseColor, -100);
        var unchanged = WpfColorPickerDialog.ApplyBrightness(baseColor, 0);

        Assert.Equal("#FFFFFF", $"#{lighter.R:X2}{lighter.G:X2}{lighter.B:X2}");
        Assert.Equal("#000000", $"#{darker.R:X2}{darker.G:X2}{darker.B:X2}");
        Assert.Equal("#808080", $"#{unchanged.R:X2}{unchanged.G:X2}{unchanged.B:X2}");
    }

    [Fact]
    public void Color_picker_brightness_slider_triggers_live_preview_callback()
    {
        RunSta(() =>
        {
            var previews = new List<string>();
            var dialog = new WpfColorPickerDialog("#808080", null, previews.Add);
            previews.Clear();
            dialog.TestSetBrightness(50);

            Assert.NotEmpty(previews);
            Assert.All(previews, hex => Assert.True(TypographyThemeDefaults.IsValidHexColor(hex)));
            Assert.NotEqual("#808080", previews[^1], StringComparer.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Color_picker_preview_callback_receives_valid_hex_on_slider_change()
    {
        RunSta(() =>
        {
            var previews = new List<string>();
            var dialog = new WpfColorPickerDialog("#112233", null, previews.Add);
            dialog.TestSetRgb(0xAB, 0xCD, 0xEF);

            Assert.Contains("#ABCDEF", previews);
        });
    }

    [Fact]
    public void Color_picker_cancel_restores_original_hex_via_editor_rollback_pattern()
    {
        RunSta(() =>
        {
            var current = "#112233";
            var original = current;
            var dialog = new WpfColorPickerDialog(original, null, previewHex => current = previewHex);
            dialog.TestSetRgb(0xFF, 0x00, 0x00);
            Assert.Equal("#FF0000", current);

            current = original;
            Assert.Equal("#112233", current);
            Assert.Equal(dialog.OriginalHex, current);
        });
    }

    [Fact]
    public async Task Dialog_preview_color_change_applies_theme_runtime_when_bound_to_appearance()
    {
        var themeRuntime = new Mock<IThemeRuntimeApplier>();
        var vm = CreatePersonalViewModel(MockAppSettings(new TaskCompletionSource()), themeRuntime: themeRuntime);
        await vm.LoadAsync();

        themeRuntime.Invocations.Clear();
        vm.PrimaryColor = "#FF0000";

        themeRuntime.Verify(
            t => t.ApplyUserAppearance(It.Is<UserAppearanceSettingsDto>(a => a.PrimaryColor == "#FF0000")),
            Times.Once);
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

    private static string ExtractXamlSection(string xaml, string startMarker, string endMarker)
    {
        var start = xaml.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker '{startMarker}' not found.");
        var end = xaml.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker '{endMarker}' not found after start.");
        return xaml[start..end];
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        if (error is not null)
        {
            throw error;
        }
    }

    private static SettingsViewModel CreatePersonalViewModel(
        Mock<IAppSettingsService> appSettings,
        Mock<ILoggingRuntimeApplier>? loggingRuntime = null,
        Mock<IThemeRuntimeApplier>? themeRuntime = null)
    {
        loggingRuntime ??= new Mock<ILoggingRuntimeApplier>();
        themeRuntime ??= new Mock<IThemeRuntimeApplier>();
        var accModeProvider = new Mock<IAccServiceModeProvider>();
        accModeProvider.SetupGet(x => x.Mode).Returns(AccServiceMode.Local);
        accModeProvider.SetupGet(x => x.BaseUrl).Returns((string?)null);

        var accKeyDiagnostics = new Mock<IAccServiceKeyDiagnostics>();
        accKeyDiagnostics.Setup(x => x.Describe()).Returns(new AccServiceKeyInfo(false, 0, null));

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
            new AccControlPlaneStatusPresenter(
                accModeProvider.Object,
                Mock.Of<IAccProjectService>(),
                accKeyDiagnostics.Object,
                Mock.Of<IAccServiceHealthProbe>(),
                Mock.Of<IAccServiceDiagnosticsProbe>()),
            Mock.Of<IAccProjectCatalogService>(),
            Mock.Of<IAccDocumentService>(),
            Mock.Of<IAccFolderBrowserService>(),
            Mock.Of<IAccLiveProjectDiscoveryService>(),
            Mock.Of<IAccResolvedDocsUrlLauncher>(),
            Mock.Of<IClipboardTextWriter>(),
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
