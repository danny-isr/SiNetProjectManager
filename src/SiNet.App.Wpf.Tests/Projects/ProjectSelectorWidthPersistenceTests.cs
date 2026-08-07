using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Projects;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Logging;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

public sealed class ProjectSelectorWidthPersistenceTests
{
    [Fact]
    public void Persisted_widths_reload_on_next_selector_instance()
    {
        var stored = UserAppSettingsDefaults.Create();
        var appSettings = new Mock<IAppSettingsService>();
        appSettings.SetupGet(s => s.UserSettingsFilePath).Returns(@"C:\temp\sinet-test-settings.json");
        appSettings
            .Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        appSettings
            .Setup(s => s.SaveUserAppSettingsAsync(It.IsAny<UserAppSettingsDto>(), It.IsAny<CancellationToken>()))
            .Callback<UserAppSettingsDto, CancellationToken>((dto, _) => stored = dto)
            .Returns(Task.CompletedTask);

        using (var first = CreateSelector(appSettings.Object))
        {
            first.ControlWidth = 410;
            first.PopupWidth = 520;
            first.FlushPersistWidths();
        }

        using var second = CreateSelector(appSettings.Object);
        Assert.Equal(410, second.ControlWidth);
        Assert.Equal(520, second.PopupWidth);
    }

    [Fact]
    public void Dispose_flushes_dirty_widths_before_debounce_elapses()
    {
        var stored = UserAppSettingsDefaults.Create();
        var appSettings = new Mock<IAppSettingsService>();
        appSettings.SetupGet(s => s.UserSettingsFilePath).Returns(@"C:\temp\sinet-test-settings.json");
        appSettings
            .Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        appSettings
            .Setup(s => s.SaveUserAppSettingsAsync(It.IsAny<UserAppSettingsDto>(), It.IsAny<CancellationToken>()))
            .Callback<UserAppSettingsDto, CancellationToken>((dto, _) => stored = dto)
            .Returns(Task.CompletedTask);

        var sut = CreateSelector(appSettings.Object);
        sut.ControlWidth = 333;
        sut.Dispose();

        Assert.Equal(333, stored.EmailProjectSelectorControlWidth);
    }

    [Fact]
    public void Json_settings_round_trips_selector_width_keys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sinet-selector-widths-{Guid.NewGuid():N}.json");
        try
        {
            var dto = JsonAppSettingsService.CreateDefaultDto() with
            {
                EmailProjectSelectorControlWidth = 390,
                EmailProjectSelectorPopupWidth = 455,
            };
            JsonAppSettingsService.WriteDto(path, dto);

            var loaded = JsonAppSettingsService.ReadDto(path);
            Assert.Equal(390, loaded.EmailProjectSelectorControlWidth);
            Assert.Equal(455, loaded.EmailProjectSelectorPopupWidth);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Di_email_window_wires_app_settings_into_selector_persist()
    {
        var stored = UserAppSettingsDefaults.Create();
        var appSettings = new Mock<IAppSettingsService>();
        appSettings.SetupGet(s => s.UserSettingsFilePath).Returns(@"C:\temp\sinet-test-settings.json");
        appSettings
            .Setup(s => s.GetUserAppSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        appSettings
            .Setup(s => s.SaveUserAppSettingsAsync(It.IsAny<UserAppSettingsDto>(), It.IsAny<CancellationToken>()))
            .Callback<UserAppSettingsDto, CancellationToken>((dto, _) => stored = dto)
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection().AddSiNetProjectContextFake();
        services.RemoveAll<IAppSettingsService>();
        services.AddSingleton<IAppSettingsService>(appSettings.Object);
        using var provider = services.BuildServiceProvider();

        using var email = provider.GetRequiredService<EmailWindowViewModel>();
        Assert.NotSame(EmailWindowViewModel.NullAppSettingsService.Instance, provider.GetRequiredService<IAppSettingsService>());
        email.ProjectSelector.ControlWidth = 375;
        email.ProjectSelector.FlushPersistWidths();

        Assert.Equal(375, stored.EmailProjectSelectorControlWidth);
    }

    private static ProjectSelectorViewModel CreateSelector(IAppSettingsService appSettings) =>
        new(
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            new InMemoryCurrentProjectContext(),
            appSettings: appSettings,
            persistSelectorWidths: true);
}
