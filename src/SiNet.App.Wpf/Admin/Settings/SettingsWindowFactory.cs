namespace SiNet.App.Wpf.Admin.Settings;

using SiNet.App.Wpf.Autodesk;
using SiNet.Application.Identity;
using SiNet.Application.Settings;

/// <summary>Opens native settings windows for personal or system-admin scope.</summary>
public interface ISettingsWindowFactory
{
    SettingsWindow CreatePersonal();

    SettingsWindow CreateSystemAdmin();
}

public sealed class SettingsWindowFactory : ISettingsWindowFactory
{
    private readonly SettingsViewModelFactory _viewModelFactory;

    public SettingsWindowFactory(SettingsViewModelFactory viewModelFactory)
    {
        _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
    }

    public SettingsWindow CreatePersonal()
        => new(_viewModelFactory.Create(SettingsSurfaceScope.Personal));

    public SettingsWindow CreateSystemAdmin()
        => new(_viewModelFactory.Create(SettingsSurfaceScope.SystemAdmin));
}

public sealed class SettingsViewModelFactory
{
    private readonly IAppSettingsService _appSettings;
    private readonly ISystemSettingsQueryService _systemQuery;
    private readonly ISystemSettingsCommandService _systemCommand;
    private readonly ILoggingSettingsCommandService _loggingCommand;
    private readonly ILoggingRuntimeApplier _loggingRuntime;
    private readonly IThemeRuntimeApplier _themeRuntime;
    private readonly IStatusColorSettingsService _statusColors;
    private readonly AccControlPlaneStatusPresenter _accControlPlaneStatusPresenter;
    private readonly IAuthorizationQueryService _authorization;
    private readonly ICurrentUserContext? _currentUser;

    public SettingsViewModelFactory(
        IAppSettingsService appSettings,
        ISystemSettingsQueryService systemQuery,
        ISystemSettingsCommandService systemCommand,
        ILoggingSettingsCommandService loggingCommand,
        ILoggingRuntimeApplier loggingRuntime,
        IThemeRuntimeApplier themeRuntime,
        IStatusColorSettingsService statusColors,
        AccControlPlaneStatusPresenter accControlPlaneStatusPresenter,
        IAuthorizationQueryService authorization,
        ICurrentUserContext? currentUser = null)
    {
        _appSettings = appSettings;
        _systemQuery = systemQuery;
        _systemCommand = systemCommand;
        _loggingCommand = loggingCommand;
        _loggingRuntime = loggingRuntime;
        _themeRuntime = themeRuntime;
        _statusColors = statusColors;
        _accControlPlaneStatusPresenter = accControlPlaneStatusPresenter;
        _authorization = authorization;
        _currentUser = currentUser;
    }

    public SettingsViewModel Create(SettingsSurfaceScope scope)
        => new(
            _appSettings,
            _systemQuery,
            _systemCommand,
            _loggingCommand,
            _loggingRuntime,
            _themeRuntime,
            _statusColors,
            _accControlPlaneStatusPresenter,
            _authorization,
            _currentUser,
            scope);
}
