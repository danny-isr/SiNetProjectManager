using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Identity;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Admin.Settings;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _appSettings;
    private readonly ISystemSettingsQueryService _systemQuery;
    private readonly ISystemSettingsCommandService _systemCommand;
    private readonly ILoggingSettingsCommandService _loggingCommand;
    private readonly ILoggingRuntimeApplier _loggingRuntime;
    private readonly IThemeRuntimeApplier _themeRuntime;
    private readonly IStatusColorSettingsService _statusColors;
    private readonly IAccServiceModeProvider _accServiceModeProvider;
    private readonly IAccServiceKeyDiagnostics _accServiceKeyDiagnostics;
    private readonly IAccServiceHealthProbe _accServiceHealthProbe;
    private readonly IAccServiceDiagnosticsProbe _accServiceDiagnosticsProbe;
    private readonly IAuthorizationQueryService _authorization;
    private readonly ICurrentUserContext? _currentUser;

    private UserLoggingSettingsDto _loadedLogging = null!;
    private UserAppearanceSettingsDto _loadedAppearance = null!;
    private UserAppearanceSettingsDto _originalAppearance = TypographyThemeDefaults.CreateDefaultAppearance();
    private bool _hasAppearanceSnapshot;
    private bool _savedSuccessfully;
    private bool _isLoadingAppearance;
    private string _summaryMessage = string.Empty;
    private bool _isBusy;
    private string _accServiceRuntimeHint =
        "מצב הריצה להלן משקף את ההוסט הנוכחי בלבד. שמירת Base URL כותבת ל-DB; restart נדרש כדי להחיל את הערך החדש.";
    private string _accServiceRuntimeModeSummary = "מצב ריצה ACC: לא נטען.";
    private string _accServiceRuntimeKeySummary = "מפתח ריצה ACC: לא נטען.";
    private string _accServiceRuntimeHealthSummary = "בריאות ריצה ACC: לא נטענה.";
    private string _accServiceRuntimeDiagnosticsSummary = "אבחון ריצה ACC: לא נטען.";

    public SettingsViewModel(
        IAppSettingsService appSettings,
        ISystemSettingsQueryService systemQuery,
        ISystemSettingsCommandService systemCommand,
        ILoggingSettingsCommandService loggingCommand,
        ILoggingRuntimeApplier loggingRuntime,
        IThemeRuntimeApplier themeRuntime,
        IStatusColorSettingsService statusColors,
        IAccServiceModeProvider accServiceModeProvider,
        IAccServiceKeyDiagnostics accServiceKeyDiagnostics,
        IAccServiceHealthProbe accServiceHealthProbe,
        IAccServiceDiagnosticsProbe accServiceDiagnosticsProbe,
        IAuthorizationQueryService authorization,
        ICurrentUserContext? currentUser,
        SettingsSurfaceScope scope)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _systemQuery = systemQuery ?? throw new ArgumentNullException(nameof(systemQuery));
        _systemCommand = systemCommand ?? throw new ArgumentNullException(nameof(systemCommand));
        _loggingCommand = loggingCommand ?? throw new ArgumentNullException(nameof(loggingCommand));
        _loggingRuntime = loggingRuntime ?? throw new ArgumentNullException(nameof(loggingRuntime));
        _themeRuntime = themeRuntime ?? throw new ArgumentNullException(nameof(themeRuntime));
        _statusColors = statusColors ?? throw new ArgumentNullException(nameof(statusColors));
        _accServiceModeProvider = accServiceModeProvider ?? throw new ArgumentNullException(nameof(accServiceModeProvider));
        _accServiceKeyDiagnostics = accServiceKeyDiagnostics ?? throw new ArgumentNullException(nameof(accServiceKeyDiagnostics));
        _accServiceHealthProbe = accServiceHealthProbe ?? throw new ArgumentNullException(nameof(accServiceHealthProbe));
        _accServiceDiagnosticsProbe = accServiceDiagnosticsProbe ?? throw new ArgumentNullException(nameof(accServiceDiagnosticsProbe));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _currentUser = currentUser;
        Scope = scope;

        AvailableFonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToList();
        LogLevels = Enum.GetNames<LogLevelDto>();

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && (CanEditPersonalSettings || CanEditSystemSettings));
        ReloadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(_ => CancelAndClose());
        BrowseLogDirectoryCommand = new RelayCommand(_ => BrowseLogDirectory(), _ => CanEditPersonalSettings);
        ProbeCentralLogPathCommand = new AsyncRelayCommand(ProbeCentralLogPathAsync, () => !IsBusy && CanEditSystemSettings);
    }

    public SettingsSurfaceScope Scope { get; }

    public IReadOnlyList<string> AvailableFonts { get; }
    public IReadOnlyList<string> LogLevels { get; }

    public ObservableCollection<UserStatusColorRowViewModel> UserStatusColors { get; } = [];
    public ObservableCollection<GlobalStatusColorRowViewModel> GlobalStatusColors { get; } = [];

    private bool _canViewPersonalSettings;
    private bool _canEditPersonalSettings;
    private bool _canViewSystemSettings;
    private bool _canEditSystemSettings;
    private bool _canViewGlobalStatusColors;
    private bool _canEditGlobalStatusColors;

    public bool CanViewPersonalSettings
    {
        get => _canViewPersonalSettings;
        private set => SetField(ref _canViewPersonalSettings, value);
    }

    public bool CanEditPersonalSettings
    {
        get => _canEditPersonalSettings;
        private set => SetField(ref _canEditPersonalSettings, value);
    }

    public bool CanViewSystemSettings
    {
        get => _canViewSystemSettings;
        private set => SetField(ref _canViewSystemSettings, value);
    }

    public bool CanEditSystemSettings
    {
        get => _canEditSystemSettings;
        private set => SetField(ref _canEditSystemSettings, value);
    }

    public bool CanViewGlobalStatusColors
    {
        get => _canViewGlobalStatusColors;
        private set => SetField(ref _canViewGlobalStatusColors, value);
    }

    public bool CanEditGlobalStatusColors
    {
        get => _canEditGlobalStatusColors;
        private set => SetField(ref _canEditGlobalStatusColors, value);
    }

    public string SummaryMessage
    {
        get => _summaryMessage;
        private set => SetField(ref _summaryMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                ReloadCommand.RaiseCanExecuteChanged();
                ProbeCentralLogPathCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _fontFamily = UserAppSettingsDefaults.FontFamily;
    private double _baseFontSize = UserAppSettingsDefaults.BaseFontSize;
    private double _textTinyScale = TypographyThemeDefaults.TextTinyScale;
    private double _textSmallScale = TypographyThemeDefaults.TextSmallScale;
    private double _textNormalScale = TypographyThemeDefaults.TextNormalScale;
    private double _textMediumScale = TypographyThemeDefaults.TextMediumScale;
    private double _textLargeScale = TypographyThemeDefaults.TextLargeScale;
    private double _textHugeScale = TypographyThemeDefaults.TextHugeScale;
    private string _foregroundColor = UserAppSettingsDefaults.ForegroundColor;
    private string _backgroundColor = UserAppSettingsDefaults.BackgroundColor;
    private string _primaryColor = TypographyThemeDefaults.PrimaryColor;
    private string _secondaryColor = TypographyThemeDefaults.SecondaryColor;
    private bool _allowMultipleInstances = UserAppSettingsDefaults.AllowMultipleInstances;
    private bool _enableAuthorizationTestMode;
    private bool _loggingEnabled;
    private string? _logDirectory;
    private double _floatingActiveOpacity = UserAppSettingsDefaults.FloatingActiveOpacity;
    private double _floatingIdleOpacity = UserAppSettingsDefaults.FloatingIdleOpacity;

    private string _defaultProjectTitle = SystemSettingsDefaults.DefaultProjectTitle;
    private string _officeManagementProjectId = SystemSettingsDefaults.OfficeManagementProjectId;
    private string _hourPriceDefault = SystemSettingsDefaults.HourPriceDefault;
    private string _inboxFolderName = SystemSettingsDefaults.InboxFolderNameFallback;
    private string? _inboxProjectName;
    private int _accViewerMaxTabs = 10;
    private string _accServiceBaseUrl = string.Empty;
    private string _accBootstrapAdminEmail = string.Empty;
    private string _accProjectTemplateName = string.Empty;
    private string _accManualUploadAllowedExtensions = SystemSettingsDefaults.AccManualUploadAllowedExtensions;
    private string _inspectionTemplatesFolderId = string.Empty;
    private string _inspectionReportsFolderId = string.Empty;
    private string _reportsOutputRoot = string.Empty;
    private string _stampTemplatePath = string.Empty;
    private string _statusLabelPassed = SystemSettingsDefaults.StatusLabelPassed;
    private string _statusLabelFailed = SystemSettingsDefaults.StatusLabelFailed;
    private string _statusLabelRecurringFailed = SystemSettingsDefaults.StatusLabelRecurringFailed;
    private string _statusLabelNotApplicable = SystemSettingsDefaults.StatusLabelNotApplicable;
    private string _ollamaBaseUrl = SystemSettingsDefaults.OllamaBaseUrl;
    private string _ollamaModel = SystemSettingsDefaults.OllamaModel;
    private string _aiModelSimple = string.Empty;
    private string _aiProviderSimple = string.Empty;
    private string _aiModelQualityCheck = string.Empty;
    private string _aiProviderQualityCheck = string.Empty;
    private string _aiModelWriting = string.Empty;
    private string _aiProviderWriting = string.Empty;
    private string _aiModelDeepAnalysis = string.Empty;
    private string _aiProviderDeepAnalysis = string.Empty;
    private string _aiConfiguredCloudModelsCsv = string.Empty;
    private string? _centralLogPath;
    private int _localRetentionDays = 14;
    private int _centralRetentionDays = 90;
    private string _clientFileLevel = LogLevelDto.Error.ToString();
    private string _clientCentralLevel = LogLevelDto.Warning.ToString();
    private string _accServiceFileLevel = LogLevelDto.Information.ToString();
    private string _accServiceCentralLevel = LogLevelDto.Warning.ToString();
    private string _syncEngineFileLevel = LogLevelDto.Information.ToString();
    private string _syncEngineCentralLevel = LogLevelDto.Warning.ToString();

    public string FontFamily
    {
        get => _fontFamily;
        set
        {
            if (SetField(ref _fontFamily, value))
            {
                ApplyAppearancePreviewIfValid();
            }
        }
    }

    public double BaseFontSize
    {
        get => _baseFontSize;
        set
        {
            if (SetField(ref _baseFontSize, value))
            {
                OnAppearanceTypographyChanged();
            }
        }
    }

    public double TextTinyScale
    {
        get => _textTinyScale;
        set
        {
            if (SetField(ref _textTinyScale, value))
            {
                OnAppearanceTypographyChanged();
            }
        }
    }

    public double TextSmallScale
    {
        get => _textSmallScale;
        set
        {
            if (SetField(ref _textSmallScale, value))
            {
                OnAppearanceTypographyChanged();
            }
        }
    }

    public double TextNormalScale
    {
        get => _textNormalScale;
        set
        {
            if (SetField(ref _textNormalScale, value))
            {
                OnAppearanceTypographyChanged();
            }
        }
    }

    public double TextMediumScale
    {
        get => _textMediumScale;
        set
        {
            if (SetField(ref _textMediumScale, value))
            {
                OnAppearanceTypographyChanged();
            }
        }
    }

    public double TextLargeScale
    {
        get => _textLargeScale;
        set
        {
            if (SetField(ref _textLargeScale, value))
            {
                OnAppearanceTypographyChanged();
            }
        }
    }

    public double TextHugeScale
    {
        get => _textHugeScale;
        set
        {
            if (SetField(ref _textHugeScale, value))
            {
                OnAppearanceTypographyChanged();
            }
        }
    }

    public string PrimaryColor
    {
        get => _primaryColor;
        set
        {
            if (SetField(ref _primaryColor, value))
            {
                if (TypographyThemeDefaults.IsValidHexColor(value))
                {
                    ApplyAppearancePreviewIfValid();
                }
            }
        }
    }

    public string SecondaryColor
    {
        get => _secondaryColor;
        set
        {
            if (SetField(ref _secondaryColor, value))
            {
                if (TypographyThemeDefaults.IsValidHexColor(value))
                {
                    ApplyAppearancePreviewIfValid();
                }
            }
        }
    }

    public string DefaultPrimaryColor => TypographyThemeDefaults.PrimaryColor;
    public string DefaultSecondaryColor => TypographyThemeDefaults.SecondaryColor;
    public string DefaultForegroundColor => UserAppSettingsDefaults.ForegroundColor;
    public string DefaultBackgroundColor => UserAppSettingsDefaults.BackgroundColor;

    public double PreviewTinyFontSize => ThemeCalculator.ComputeFontSize(BaseFontSize, TextTinyScale);
    public double PreviewSmallFontSize => ThemeCalculator.ComputeFontSize(BaseFontSize, TextSmallScale);
    public double PreviewNormalFontSize => ThemeCalculator.ComputeFontSize(BaseFontSize, TextNormalScale);
    public double PreviewMediumFontSize => ThemeCalculator.ComputeFontSize(BaseFontSize, TextMediumScale);
    public double PreviewLargeFontSize => ThemeCalculator.ComputeFontSize(BaseFontSize, TextLargeScale);
    public double PreviewHugeFontSize => ThemeCalculator.ComputeFontSize(BaseFontSize, TextHugeScale);

    public string ForegroundColor
    {
        get => _foregroundColor;
        set
        {
            if (SetField(ref _foregroundColor, value))
            {
                if (TypographyThemeDefaults.IsValidHexColor(value))
                {
                    ApplyAppearancePreviewIfValid();
                }
            }
        }
    }

    public string BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (SetField(ref _backgroundColor, value))
            {
                if (TypographyThemeDefaults.IsValidHexColor(value))
                {
                    ApplyAppearancePreviewIfValid();
                }
            }
        }
    }

    public bool AllowMultipleInstances
    {
        get => _allowMultipleInstances;
        set => SetField(ref _allowMultipleInstances, value);
    }

    public bool EnableAuthorizationTestMode
    {
        get => _enableAuthorizationTestMode;
        set => SetField(ref _enableAuthorizationTestMode, value);
    }

    public bool LoggingEnabled
    {
        get => _loggingEnabled;
        set => SetField(ref _loggingEnabled, value);
    }

    public string? LogDirectory
    {
        get => _logDirectory;
        set
        {
            if (SetField(ref _logDirectory, value))
            {
                OnPropertyChanged(nameof(LogDirectoryDisplay));
            }
        }
    }

    public string LogDirectoryDisplay =>
        string.IsNullOrWhiteSpace(LogDirectory)
            ? $"(ברירת מחדל: {LoggingSettingsMetadata.AppLoggerDefaultLocalLogDirectory})"
            : LogDirectory;

    public double FloatingActiveOpacity
    {
        get => _floatingActiveOpacity;
        set => SetField(ref _floatingActiveOpacity, value);
    }

    public double FloatingIdleOpacity
    {
        get => _floatingIdleOpacity;
        set => SetField(ref _floatingIdleOpacity, value);
    }

    public string DefaultProjectTitle
    {
        get => _defaultProjectTitle;
        set => SetField(ref _defaultProjectTitle, value);
    }

    public string OfficeManagementProjectId
    {
        get => _officeManagementProjectId;
        set => SetField(ref _officeManagementProjectId, value);
    }

    public string HourPriceDefault
    {
        get => _hourPriceDefault;
        set => SetField(ref _hourPriceDefault, value);
    }

    public string InboxFolderName
    {
        get => _inboxFolderName;
        set => SetField(ref _inboxFolderName, value);
    }

    public string? InboxProjectName
    {
        get => _inboxProjectName;
        set => SetField(ref _inboxProjectName, value);
    }

    public int AccViewerMaxTabs
    {
        get => _accViewerMaxTabs;
        set => SetField(ref _accViewerMaxTabs, value);
    }

    public string AccServiceBaseUrl
    {
        get => _accServiceBaseUrl;
        set => SetField(ref _accServiceBaseUrl, value);
    }

    public string AccBootstrapAdminEmail
    {
        get => _accBootstrapAdminEmail;
        set => SetField(ref _accBootstrapAdminEmail, value);
    }

    public string AccProjectTemplateName
    {
        get => _accProjectTemplateName;
        set => SetField(ref _accProjectTemplateName, value);
    }

    public string AccManualUploadAllowedExtensions
    {
        get => _accManualUploadAllowedExtensions;
        set => SetField(ref _accManualUploadAllowedExtensions, value);
    }

    public string InspectionTemplatesFolderId
    {
        get => _inspectionTemplatesFolderId;
        set => SetField(ref _inspectionTemplatesFolderId, value);
    }

    public string InspectionReportsFolderId
    {
        get => _inspectionReportsFolderId;
        set => SetField(ref _inspectionReportsFolderId, value);
    }

    public string ReportsOutputRoot
    {
        get => _reportsOutputRoot;
        set => SetField(ref _reportsOutputRoot, value);
    }

    public string StampTemplatePath
    {
        get => _stampTemplatePath;
        set => SetField(ref _stampTemplatePath, value);
    }

    public string StatusLabelPassed
    {
        get => _statusLabelPassed;
        set => SetField(ref _statusLabelPassed, value);
    }

    public string StatusLabelFailed
    {
        get => _statusLabelFailed;
        set => SetField(ref _statusLabelFailed, value);
    }

    public string StatusLabelRecurringFailed
    {
        get => _statusLabelRecurringFailed;
        set => SetField(ref _statusLabelRecurringFailed, value);
    }

    public string StatusLabelNotApplicable
    {
        get => _statusLabelNotApplicable;
        set => SetField(ref _statusLabelNotApplicable, value);
    }

    public string OllamaBaseUrl
    {
        get => _ollamaBaseUrl;
        set => SetField(ref _ollamaBaseUrl, value);
    }

    public string OllamaModel
    {
        get => _ollamaModel;
        set => SetField(ref _ollamaModel, value);
    }

    public string AiModelSimple
    {
        get => _aiModelSimple;
        set => SetField(ref _aiModelSimple, value);
    }

    public string AiProviderSimple
    {
        get => _aiProviderSimple;
        set => SetField(ref _aiProviderSimple, value);
    }

    public string AiModelQualityCheck
    {
        get => _aiModelQualityCheck;
        set => SetField(ref _aiModelQualityCheck, value);
    }

    public string AiProviderQualityCheck
    {
        get => _aiProviderQualityCheck;
        set => SetField(ref _aiProviderQualityCheck, value);
    }

    public string AiModelWriting
    {
        get => _aiModelWriting;
        set => SetField(ref _aiModelWriting, value);
    }

    public string AiProviderWriting
    {
        get => _aiProviderWriting;
        set => SetField(ref _aiProviderWriting, value);
    }

    public string AiModelDeepAnalysis
    {
        get => _aiModelDeepAnalysis;
        set => SetField(ref _aiModelDeepAnalysis, value);
    }

    public string AiProviderDeepAnalysis
    {
        get => _aiProviderDeepAnalysis;
        set => SetField(ref _aiProviderDeepAnalysis, value);
    }

    public string AiConfiguredCloudModelsCsv
    {
        get => _aiConfiguredCloudModelsCsv;
        set => SetField(ref _aiConfiguredCloudModelsCsv, value);
    }

    public string? CentralLogPath
    {
        get => _centralLogPath;
        set => SetField(ref _centralLogPath, value);
    }

    public int LocalRetentionDays
    {
        get => _localRetentionDays;
        set => SetField(ref _localRetentionDays, value);
    }

    public int CentralRetentionDays
    {
        get => _centralRetentionDays;
        set => SetField(ref _centralRetentionDays, value);
    }

    public string ClientFileLevel
    {
        get => _clientFileLevel;
        set => SetField(ref _clientFileLevel, value);
    }

    public string ClientCentralLevel
    {
        get => _clientCentralLevel;
        set => SetField(ref _clientCentralLevel, value);
    }

    public string AccServiceFileLevel
    {
        get => _accServiceFileLevel;
        set => SetField(ref _accServiceFileLevel, value);
    }

    public string AccServiceCentralLevel
    {
        get => _accServiceCentralLevel;
        set => SetField(ref _accServiceCentralLevel, value);
    }

    public string SyncEngineFileLevel
    {
        get => _syncEngineFileLevel;
        set => SetField(ref _syncEngineFileLevel, value);
    }

    public string SyncEngineCentralLevel
    {
        get => _syncEngineCentralLevel;
        set => SetField(ref _syncEngineCentralLevel, value);
    }

    public string RestartRequiredHint =>
        "הגדרות גלובליות נשמרות ב-DB; consumers שקוראים ב-bootstrap דורשים restart.";

    public string CentralLoggingRestartHint => CentralLoggingSettingsDto.RequiresRestartMessage;

    public string AccServiceRuntimeHint
    {
        get => _accServiceRuntimeHint;
        private set => SetField(ref _accServiceRuntimeHint, value);
    }

    public string AccServiceRuntimeModeSummary
    {
        get => _accServiceRuntimeModeSummary;
        private set => SetField(ref _accServiceRuntimeModeSummary, value);
    }

    public string AccServiceRuntimeKeySummary
    {
        get => _accServiceRuntimeKeySummary;
        private set => SetField(ref _accServiceRuntimeKeySummary, value);
    }

    public string AccServiceRuntimeHealthSummary
    {
        get => _accServiceRuntimeHealthSummary;
        private set => SetField(ref _accServiceRuntimeHealthSummary, value);
    }

    public string AccServiceRuntimeDiagnosticsSummary
    {
        get => _accServiceRuntimeDiagnosticsSummary;
        private set => SetField(ref _accServiceRuntimeDiagnosticsSummary, value);
    }

    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseLogDirectoryCommand { get; }
    public AsyncRelayCommand ProbeCentralLogPathCommand { get; }

    public event Action<bool>? RequestClose;

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await RefreshPermissionFlagsAsync().ConfigureAwait(true);

            if (CanViewPersonalSettings)
            {
                var user = await _appSettings.GetUserAppSettingsAsync().ConfigureAwait(true);
                _isLoadingAppearance = true;
                try
                {
                    ApplyUserSettings(user);
                    _originalAppearance = user.Appearance;
                    _loadedAppearance = user.Appearance;
                    _hasAppearanceSnapshot = true;
                    _savedSuccessfully = false;
                }
                finally
                {
                    _isLoadingAppearance = false;
                }

                _themeRuntime.ApplyUserAppearance(user.Appearance);

                _loadedLogging = user.Logging;

                UserStatusColors.Clear();
                if (_currentUser?.UserId is int userId)
                {
                    var personal = await _statusColors.GetUserStatusColorsAsync(userId).ConfigureAwait(true);
                    foreach (var row in personal)
                    {
                        UserStatusColors.Add(new UserStatusColorRowViewModel(row, userId, _statusColors, ReloadUserColorsAsync));
                    }
                }
            }

            if (CanViewSystemSettings)
            {
                var system = await _systemQuery.GetSystemSettingsAsync().ConfigureAwait(true);
                ApplySystemSettings(system);

                GlobalStatusColors.Clear();
                var global = await _statusColors.GetGlobalStatusColorsAsync().ConfigureAwait(true);
                foreach (var row in global)
                {
                    GlobalStatusColors.Add(new GlobalStatusColorRowViewModel(row, _statusColors));
                }

                await RefreshAccRuntimeStatusAsync().ConfigureAwait(true);
            }

            SummaryMessage = CanViewPersonalSettings && CanViewSystemSettings
                ? "ההגדרות נטענו."
                : CanViewPersonalSettings
                    ? "ההגדרות האישיות נטענו."
                    : CanViewSystemSettings
                        ? "הגדרות המערכת נטענו."
                        : "אין הרשאה לצפות בהגדרות במסך זה.";
        }
        catch (Exception ex)
        {
            SummaryMessage = AppErrorReporter.FormatUserMessage(ex, "טעינת הגדרות");
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task RefreshPermissionFlagsAsync(CancellationToken cancellationToken = default)
    {
        var hasUser = _currentUser?.UserId is not null;
        var isAdmin = await _authorization
            .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.SystemSettingsWrite, cancellationToken)
            .ConfigureAwait(false);

        var personalScope = Scope == SettingsSurfaceScope.Personal;
        var systemScope = Scope == SettingsSurfaceScope.SystemAdmin;

        CanViewPersonalSettings = personalScope && hasUser;
        CanEditPersonalSettings = CanViewPersonalSettings;
        CanViewSystemSettings = systemScope && isAdmin;
        CanEditSystemSettings = CanViewSystemSettings;
        CanViewGlobalStatusColors = CanViewSystemSettings;
        CanEditGlobalStatusColors = CanEditSystemSettings;

        SaveCommand.RaiseCanExecuteChanged();
        BrowseLogDirectoryCommand.RaiseCanExecuteChanged();
        ProbeCentralLogPathCommand.RaiseCanExecuteChanged();
    }

    private async Task SaveAsync()
    {
        if (!Validate(out var error))
        {
            SummaryMessage = error;
            return;
        }

        IsBusy = true;
        try
        {
            var messages = new List<string>();

            if (CanEditPersonalSettings)
            {
                var userDto = BuildUserDto();
                await _appSettings.SaveUserAppSettingsAsync(userDto).ConfigureAwait(true);

                if (LoggingChanged(userDto.Logging))
                {
                    _loggingRuntime.ApplyUserLogging(userDto.Logging);
                    _loadedLogging = userDto.Logging;
                }

                _loadedAppearance = userDto.Appearance;
                _originalAppearance = userDto.Appearance;
                _savedSuccessfully = true;

                messages.Add("הגדרות אישיות נשמרו.");
            }

            if (CanEditSystemSettings)
            {
                await _systemCommand.SaveSystemSettingsAsync(BuildSystemDto()).ConfigureAwait(true);
                messages.Add("הגדרות מערכת נשמרו. " + CentralLoggingSettingsDto.RequiresRestartMessage);
            }

            SummaryMessage = string.Join(" ", messages);
            RequestClose?.Invoke(true);
        }
        catch (UnauthorizedAccessException)
        {
            SummaryMessage = "אין הרשאה לשמור הגדרות מערכת.";
        }
        catch (Exception ex)
        {
            SummaryMessage = AppErrorReporter.FormatUserMessage(ex, "שמירת הגדרות");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool Validate(out string error)
    {
        if (CanEditPersonalSettings && !ValidatePersonal(out error))
        {
            return false;
        }

        if (CanEditSystemSettings && !ValidateSystem(out error))
        {
            return false;
        }

        error = string.Empty;
        return CanEditPersonalSettings || CanEditSystemSettings;
    }

    private bool ValidatePersonal(out string error)
    {
        foreach (var hex in new[] { ForegroundColor, BackgroundColor, PrimaryColor, SecondaryColor })
        {
            if (!TypographyThemeDefaults.IsValidHexColor(hex))
            {
                error = $"צבע לא תקין: {hex}";
                return false;
            }
        }

        if (BaseFontSize < 8 || BaseFontSize > 32)
        {
            error = "גודל פונט בסיס חייב להיות בין 8 ל-32.";
            return false;
        }

        if (!TypographyThemeDefaults.TryValidateScales(
                TextTinyScale,
                TextSmallScale,
                TextNormalScale,
                TextMediumScale,
                TextLargeScale,
                TextHugeScale,
                out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool ValidateSystem(out string error)
    {
        if (string.IsNullOrWhiteSpace(DefaultProjectTitle))
        {
            error = "נא להזין שם פרויקט ברירת מחדל.";
            return false;
        }

        if (!decimal.TryParse(HourPriceDefault, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price <= 0)
        {
            error = "מחיר שעה חייב להיות מספר חיובי.";
            return false;
        }

        if (!int.TryParse(OfficeManagementProjectId, out var officeId) || officeId <= 0)
        {
            error = "מספר פרויקט ניהול משרד חייב להיות מספר חיובי.";
            return false;
        }

        if (AccViewerMaxTabs <= 0)
        {
            error = "מגבלת טאבים ACC חייבת להיות מספר חיובי.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(AccServiceBaseUrl)
            && (!Uri.TryCreate(AccServiceBaseUrl.Trim(), UriKind.Absolute, out var uri)
                || string.IsNullOrWhiteSpace(uri.Host)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            error = "נא להזין כתובת URL תקינה לשירות ACC, למשל https://SI-WIN-2K19:8443, או להשאיר ריק למצב מקומי.";
            return false;
        }

        if (LocalRetentionDays <= 0 || CentralRetentionDays <= 0)
        {
            error = "ימי שמירת לוג חייבים להיות מספרים חיוביים.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private UserAppSettingsDto BuildUserDto()
    {
        var defaults = UserAppSettingsDefaults.Create();
        return new UserAppSettingsDto(
            BuildAppearanceDto(),
            new UserBehaviorSettingsDto(AllowMultipleInstances),
            new UserLoggingSettingsDto(
                LoggingEnabled,
                string.IsNullOrWhiteSpace(LogDirectory) ? null : LogDirectory.Trim(),
                LoggingSettingsMetadata.BootstrapDefaultLocalLogDirectory,
                LoggingSettingsMetadata.AppLoggerDefaultLocalLogDirectory),
            new UserFloatingWindowOpacityDto(
                Math.Clamp(FloatingActiveOpacity, 0.1, 1.0),
                Math.Clamp(FloatingIdleOpacity, 0.1, 1.0)),
            defaults.FloatingTasks,
            defaults.FloatingInspection,
            EnableAuthorizationTestMode);
    }

    private UserAppearanceSettingsDto BuildAppearanceDto() => new(
        FontFamily,
        BaseFontSize,
        TextTinyScale,
        TextSmallScale,
        TextNormalScale,
        TextMediumScale,
        TextLargeScale,
        TextHugeScale,
        ForegroundColor.Trim(),
        BackgroundColor.Trim(),
        PrimaryColor.Trim(),
        SecondaryColor.Trim());

    private SystemSettingsDto BuildSystemDto() => new(
        new EmailOfficeSystemSettingsDto(
            DefaultProjectTitle.Trim(),
            OfficeManagementProjectId.Trim(),
            HourPriceDefault.Trim(),
            InboxFolderName.Trim(),
            string.IsNullOrWhiteSpace(InboxProjectName) ? null : InboxProjectName.Trim(),
            AccViewerMaxTabs),
        new AccSystemSettingsDto(
            NormalizeAccServiceBaseUrl(AccServiceBaseUrl),
            AccBootstrapAdminEmail.Trim(),
            AccProjectTemplateName.Trim(),
            AccManualUploadAllowedExtensions.Trim()),
        new InspectionSystemSettingsDto(
            InspectionTemplatesFolderId.Trim(),
            InspectionReportsFolderId.Trim(),
            ReportsOutputRoot.Trim(),
            StampTemplatePath.Trim()),
        new InspectionStatusLabelsDto(
            StatusLabelPassed.Trim(),
            StatusLabelFailed.Trim(),
            StatusLabelRecurringFailed.Trim(),
            StatusLabelNotApplicable.Trim()),
        new AiSystemSettingsDto(
            OllamaBaseUrl.Trim(),
            OllamaModel.Trim(),
            new AiModelLevelSelectionDto(AiModelSimple.Trim(), AiProviderSimple.Trim()),
            new AiModelLevelSelectionDto(AiModelQualityCheck.Trim(), AiProviderQualityCheck.Trim()),
            new AiModelLevelSelectionDto(AiModelWriting.Trim(), AiProviderWriting.Trim()),
            new AiModelLevelSelectionDto(AiModelDeepAnalysis.Trim(), AiProviderDeepAnalysis.Trim()),
            AiConfiguredCloudModelsCsv.Trim()),
        new CentralLoggingSettingsDto(
            string.IsNullOrWhiteSpace(CentralLogPath) ? null : CentralLogPath.Trim(),
            LocalRetentionDays,
            CentralRetentionDays,
            new AppLogLevelsDto(ParseLevel(ClientFileLevel), ParseLevel(ClientCentralLevel)),
            new AppLogLevelsDto(ParseLevel(AccServiceFileLevel), ParseLevel(AccServiceCentralLevel)),
            new AppLogLevelsDto(ParseLevel(SyncEngineFileLevel), ParseLevel(SyncEngineCentralLevel)),
            !string.IsNullOrWhiteSpace(CentralLogPath)));

    private void ApplyUserSettings(UserAppSettingsDto user)
    {
        FontFamily = user.Appearance.FontFamily;
        BaseFontSize = user.Appearance.BaseFontSize;
        TextTinyScale = user.Appearance.TextTinyScale;
        TextSmallScale = user.Appearance.TextSmallScale;
        TextNormalScale = user.Appearance.TextNormalScale;
        TextMediumScale = user.Appearance.TextMediumScale;
        TextLargeScale = user.Appearance.TextLargeScale;
        TextHugeScale = user.Appearance.TextHugeScale;
        ForegroundColor = user.Appearance.ForegroundColor;
        BackgroundColor = user.Appearance.BackgroundColor;
        PrimaryColor = user.Appearance.PrimaryColor;
        SecondaryColor = user.Appearance.SecondaryColor;
        AllowMultipleInstances = user.Behavior.AllowMultipleInstances;
        EnableAuthorizationTestMode = user.EnableAuthorizationTestMode;
        LoggingEnabled = user.Logging.LoggingEnabled;
        LogDirectory = user.Logging.LogDirectory;
        FloatingActiveOpacity = user.FloatingOpacity.ActiveOpacity;
        FloatingIdleOpacity = user.FloatingOpacity.IdleOpacity;
    }

    private void ApplySystemSettings(SystemSettingsDto system)
    {
        DefaultProjectTitle = system.EmailOffice.DefaultProjectTitle;
        OfficeManagementProjectId = system.EmailOffice.OfficeManagementProjectId;
        HourPriceDefault = system.EmailOffice.HourPriceDefault;
        InboxFolderName = system.EmailOffice.InboxFolderName;
        InboxProjectName = system.EmailOffice.InboxProjectName;
        AccViewerMaxTabs = system.EmailOffice.AccViewerMaxTabs;
        AccServiceBaseUrl = system.Acc.AccServiceBaseUrl;
        AccBootstrapAdminEmail = system.Acc.AccBootstrapAdminEmail;
        AccProjectTemplateName = system.Acc.AccProjectTemplateName;
        AccManualUploadAllowedExtensions = system.Acc.AccManualUploadAllowedExtensions;
        InspectionTemplatesFolderId = system.Inspection.InspectionTemplatesFolderId;
        InspectionReportsFolderId = system.Inspection.InspectionReportsFolderId;
        ReportsOutputRoot = system.Inspection.ReportsOutputRoot;
        StampTemplatePath = system.Inspection.StampTemplatePath;
        StatusLabelPassed = system.StatusLabels.Passed;
        StatusLabelFailed = system.StatusLabels.Failed;
        StatusLabelRecurringFailed = system.StatusLabels.RecurringFailed;
        StatusLabelNotApplicable = system.StatusLabels.NotApplicable;
        OllamaBaseUrl = system.Ai.OllamaBaseUrl;
        OllamaModel = system.Ai.OllamaModel;
        AiModelSimple = system.Ai.Simple.Model;
        AiProviderSimple = system.Ai.Simple.Provider;
        AiModelQualityCheck = system.Ai.QualityCheck.Model;
        AiProviderQualityCheck = system.Ai.QualityCheck.Provider;
        AiModelWriting = system.Ai.Writing.Model;
        AiProviderWriting = system.Ai.Writing.Provider;
        AiModelDeepAnalysis = system.Ai.DeepAnalysis.Model;
        AiProviderDeepAnalysis = system.Ai.DeepAnalysis.Provider;
        AiConfiguredCloudModelsCsv = system.Ai.ConfiguredCloudModelsCsv;

        var log = system.Logging;
        CentralLogPath = log.CentralLogPath;
        LocalRetentionDays = log.LocalRetentionDays;
        CentralRetentionDays = log.CentralRetentionDays;
        ClientFileLevel = log.Client.FileLevel.ToString();
        ClientCentralLevel = log.Client.CentralLevel.ToString();
        AccServiceFileLevel = log.AccService.FileLevel.ToString();
        AccServiceCentralLevel = log.AccService.CentralLevel.ToString();
        SyncEngineFileLevel = log.SyncEngine.FileLevel.ToString();
        SyncEngineCentralLevel = log.SyncEngine.CentralLevel.ToString();
    }

    private bool LoggingChanged(UserLoggingSettingsDto current)
        => current.LoggingEnabled != _loadedLogging.LoggingEnabled
           || !string.Equals(current.LogDirectory, _loadedLogging.LogDirectory, StringComparison.Ordinal);

    internal bool SavedSuccessfully => _savedSuccessfully;

    internal void RollbackAppearanceIfNeeded()
    {
        if (_savedSuccessfully || !_hasAppearanceSnapshot || !CanEditPersonalSettings)
        {
            return;
        }

        _isLoadingAppearance = true;
        try
        {
            _themeRuntime.ApplyUserAppearance(_originalAppearance);
        }
        finally
        {
            _isLoadingAppearance = false;
        }
    }

    private void CancelAndClose()
    {
        RollbackAppearanceIfNeeded();
        RequestClose?.Invoke(false);
    }

    private void OnAppearanceTypographyChanged()
    {
        NotifyPreviewTypographyChanged();
        ApplyAppearancePreviewIfValid();
    }

    private void ApplyAppearancePreviewIfValid()
    {
        if (_isLoadingAppearance || !CanEditPersonalSettings || !_hasAppearanceSnapshot)
        {
            return;
        }

        _themeRuntime.ApplyUserAppearance(BuildAppearancePreviewDto());
    }

    private UserAppearanceSettingsDto BuildAppearancePreviewDto() => new(
        FontFamily,
        BaseFontSize,
        TextTinyScale,
        TextSmallScale,
        TextNormalScale,
        TextMediumScale,
        TextLargeScale,
        TextHugeScale,
        ResolvePreviewColor(ForegroundColor, _originalAppearance.ForegroundColor),
        ResolvePreviewColor(BackgroundColor, _originalAppearance.BackgroundColor),
        ResolvePreviewColor(PrimaryColor, _originalAppearance.PrimaryColor),
        ResolvePreviewColor(SecondaryColor, _originalAppearance.SecondaryColor));

    private static string ResolvePreviewColor(string current, string fallback)
        => TypographyThemeDefaults.IsValidHexColor(current) ? current.Trim() : fallback;

    private async Task ProbeCentralLogPathAsync()
    {
        if (!CanEditSystemSettings)
        {
            SummaryMessage = "אין הרשאה לבדוק נתיב לוג מרכזי.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CentralLogPath))
        {
            SummaryMessage = "נא להזין נתיב לוג מרכזי.";
            return;
        }

        IsBusy = true;
        try
        {
            var ok = await _loggingCommand.ProbeCentralLogPathAsync(CentralLogPath).ConfigureAwait(true);
            SummaryMessage = ok ? "נתיב הלוג המרכזי נגיש לכתיבה." : "לא ניתן לכתוב לנתיב הלוג המרכזי.";
        }
        catch (Exception ex)
        {
            SummaryMessage = AppErrorReporter.FormatUserMessage(ex, "בדיקת נתיב לוג");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BrowseLogDirectory()
    {
        var dialog = new OpenFolderDialog { Title = "בחר תיקיית לוג" };
        if (dialog.ShowDialog() == true)
        {
            LogDirectory = dialog.FolderName;
        }
    }

    private async Task ReloadUserColorsAsync()
    {
        if (_currentUser?.UserId is not int userId)
        {
            return;
        }

        UserStatusColors.Clear();
        var personal = await _statusColors.GetUserStatusColorsAsync(userId).ConfigureAwait(true);
        foreach (var row in personal)
        {
            UserStatusColors.Add(new UserStatusColorRowViewModel(row, userId, _statusColors, ReloadUserColorsAsync));
        }
    }

    private async Task RefreshAccRuntimeStatusAsync()
    {
        var mode = _accServiceModeProvider.Mode;
        var baseUrl = _accServiceModeProvider.BaseUrl;

        AccServiceRuntimeModeSummary = mode switch
        {
            AccServiceMode.Remote when !string.IsNullOrWhiteSpace(baseUrl)
                => $"מצב ריצה ACC: שירות מרכזי ({baseUrl})",
            _ => "מצב ריצה ACC: מקומי (AccService:BaseUrl לא מוגדר בהוסט הנוכחי)",
        };

        var keyInfo = _accServiceKeyDiagnostics.Describe();
        AccServiceRuntimeKeySummary = keyInfo.HasApiKey
            ? $"מפתח ריצה ACC: קיים ב-Vault, אורך {keyInfo.KeyLength}, hash {keyInfo.KeyHashPrefix}"
            : "מפתח ריצה ACC: לא הוגדר ב-Vault.";

        if (mode != AccServiceMode.Remote || string.IsNullOrWhiteSpace(baseUrl))
        {
            AccServiceRuntimeHealthSummary = "בריאות ריצה ACC: לא רלוונטי במצב מקומי.";
            AccServiceRuntimeDiagnosticsSummary = "אבחון ריצה ACC: מצב מקומי, ללא קריאת /v1/acc/diag.";
            return;
        }

        var health = await _accServiceHealthProbe.CheckAsync().ConfigureAwait(true);
        AccServiceRuntimeHealthSummary = health.State switch
        {
            AccServiceHealthState.Online => $"בריאות ריצה ACC: זמין ({health.Endpoint})",
            AccServiceHealthState.NotConfigured => "בריאות ריצה ACC: לא מוגדר.",
            _ => $"בריאות ריצה ACC: לא זמין ({health.Detail ?? "ללא פירוט"})",
        };

        var diagnostics = await _accServiceDiagnosticsProbe.ProbeAsync().ConfigureAwait(true);
        if (!diagnostics.Reachable)
        {
            AccServiceRuntimeDiagnosticsSummary =
                $"אבחון ריצה ACC: לא זמין. Autodesk={diagnostics.AutodeskDetail ?? "ללא פירוט"}; DB={diagnostics.DbDetail ?? "ללא פירוט"}";
            return;
        }

        var keySource = string.IsNullOrWhiteSpace(diagnostics.KeySource) ? "unknown" : diagnostics.KeySource;
        var windowsUser = string.IsNullOrWhiteSpace(diagnostics.WindowsUser) ? "unknown" : diagnostics.WindowsUser;
        var keyHash = string.IsNullOrWhiteSpace(diagnostics.KeyHashPrefix) ? "(none)" : diagnostics.KeyHashPrefix;
        AccServiceRuntimeDiagnosticsSummary =
            $"אבחון ריצה ACC: user={windowsUser}; keySource={keySource}; keyHash={keyHash}; Autodesk={(diagnostics.AutodeskOk ? "ok" : "fail")}; DB={(diagnostics.DbOk ? "ok" : "fail")}";
    }

    private static string NormalizeAccServiceBaseUrl(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed.TrimEnd('/');
    }

    private static LogLevelDto ParseLevel(string value)
        => Enum.TryParse<LogLevelDto>(value, ignoreCase: true, out var level) ? level : LogLevelDto.Error;

    private void NotifyPreviewTypographyChanged()
    {
        OnPropertyChanged(nameof(PreviewTinyFontSize));
        OnPropertyChanged(nameof(PreviewSmallFontSize));
        OnPropertyChanged(nameof(PreviewNormalFontSize));
        OnPropertyChanged(nameof(PreviewMediumFontSize));
        OnPropertyChanged(nameof(PreviewLargeFontSize));
        OnPropertyChanged(nameof(PreviewHugeFontSize));
    }
}

public sealed class UserStatusColorRowViewModel : ObservableObject
{
    private readonly int _userId;
    private readonly IStatusColorSettingsService _service;
    private readonly Func<Task> _reload;

    public UserStatusColorRowViewModel(
        UserStatusColorEntryDto dto,
        int userId,
        IStatusColorSettingsService service,
        Func<Task> reload)
    {
        _userId = userId;
        _service = service;
        _reload = reload;
        StatusId = dto.StatusId;
        StatusName = dto.StatusName;
        DefaultColorHex = dto.DefaultColorHex;
        _colorHex = dto.ResolvedColorHex;
        HasOverride = dto.HasOverride;
        SaveOverrideCommand = new AsyncRelayCommand(SaveOverrideAsync);
        ResetCommand = new AsyncRelayCommand(ResetAsync);
    }

    public int StatusId { get; }
    public string StatusName { get; }
    public string DefaultColorHex { get; }
    public bool HasOverride { get; private set; }

    private string _colorHex;

    public string ColorHex
    {
        get => _colorHex;
        set => SetField(ref _colorHex, value);
    }

    public AsyncRelayCommand SaveOverrideCommand { get; }
    public AsyncRelayCommand ResetCommand { get; }

    private async Task SaveOverrideAsync()
    {
        await _service.SetUserOverrideAsync(_userId, StatusId, ColorHex).ConfigureAwait(true);
        await _reload().ConfigureAwait(true);
    }

    private async Task ResetAsync()
    {
        await _service.RemoveUserOverrideAsync(_userId, StatusId).ConfigureAwait(true);
        await _reload().ConfigureAwait(true);
    }
}

public sealed class GlobalStatusColorRowViewModel : ObservableObject
{
    private readonly IStatusColorSettingsService _service;

    public GlobalStatusColorRowViewModel(GlobalStatusColorEntryDto dto, IStatusColorSettingsService service)
    {
        _service = service;
        StatusId = dto.StatusId;
        StatusName = dto.StatusName;
        DefaultColorHex = dto.ColorHex;
        _colorHex = dto.ColorHex;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public int StatusId { get; }
    public string StatusName { get; }
    public string DefaultColorHex { get; }

    private string _colorHex;

    public string ColorHex
    {
        get => _colorHex;
        set => SetField(ref _colorHex, value);
    }

    public AsyncRelayCommand SaveCommand { get; }

    private Task SaveAsync() => _service.SetGlobalDefaultColorAsync(StatusId, ColorHex);
}
