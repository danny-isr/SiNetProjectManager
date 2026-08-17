using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Admin.UserGroups;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Identity;
using SiNet.Application.ProjectWork;
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
    private readonly AccControlPlaneStatusPresenter _accControlPlaneStatusPresenter;
    private readonly IAccProjectCatalogService _accProjectCatalogService;
    private readonly IAuthorizationQueryService _authorization;
    private readonly ICurrentUserContext? _currentUser;
    private readonly IUserGroupsWindowFactory? _userGroupsWindowFactory;
    private readonly IProjectWorkScanExclusionPolicy? _scanExclusionPolicy;

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
    private string _accServiceRuntimeProjectsSummary = "פרויקטי ריצה ACC מוכרים: לא נטענו.";
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
        AccControlPlaneStatusPresenter accControlPlaneStatusPresenter,
        IAccProjectCatalogService accProjectCatalogService,
        IAccDocumentService accDocumentService,
        IAccFolderBrowserService accFolderBrowserService,
        IAccProjectTreeSearchService accProjectTreeSearchService,
        IAccLiveProjectDiscoveryService accLiveProjectDiscoveryService,
        IAccResolvedDocsUrlLauncher resolvedDocsUrlLauncher,
        IClipboardTextWriter clipboardTextWriter,
        IAuthorizationQueryService authorization,
        ICurrentUserContext? currentUser,
        SettingsSurfaceScope scope,
        IUserGroupsWindowFactory? userGroupsWindowFactory = null,
        IProjectWorkScanExclusionPolicy? scanExclusionPolicy = null)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _systemQuery = systemQuery ?? throw new ArgumentNullException(nameof(systemQuery));
        _systemCommand = systemCommand ?? throw new ArgumentNullException(nameof(systemCommand));
        _loggingCommand = loggingCommand ?? throw new ArgumentNullException(nameof(loggingCommand));
        _loggingRuntime = loggingRuntime ?? throw new ArgumentNullException(nameof(loggingRuntime));
        _themeRuntime = themeRuntime ?? throw new ArgumentNullException(nameof(themeRuntime));
        _statusColors = statusColors ?? throw new ArgumentNullException(nameof(statusColors));
        _accControlPlaneStatusPresenter = accControlPlaneStatusPresenter ?? throw new ArgumentNullException(nameof(accControlPlaneStatusPresenter));
        _accProjectCatalogService = accProjectCatalogService ?? throw new ArgumentNullException(nameof(accProjectCatalogService));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _currentUser = currentUser;
        _userGroupsWindowFactory = userGroupsWindowFactory;
        _scanExclusionPolicy = scanExclusionPolicy;
        Scope = scope;

        AvailableFonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToList();
        LogLevels = Enum.GetNames<LogLevelDto>();
        AccBrowser = new AccReadOnlyDocumentBrowserViewModel(
            accDocumentService,
            accFolderBrowserService,
            accProjectTreeSearchService,
            accLiveProjectDiscoveryService,
            resolvedDocsUrlLauncher,
            clipboardTextWriter,
            canInteract: () => CanViewSystemSettings,
            isHostBusy: () => IsBusy,
            summaryMessageSink: message => SummaryMessage = message);
        AccBrowser.PropertyChanged += (_, e) => ForwardAccBrowserPropertyChanged(e.PropertyName);

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && !AccBrowser.IsBusy && (CanEditPersonalSettings || CanEditSystemSettings));
        ReloadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && !AccBrowser.IsBusy);
        CancelCommand = new RelayCommand(_ => CancelAndClose());
        BrowseLogDirectoryCommand = new RelayCommand(_ => BrowseLogDirectory(), _ => !AccBrowser.IsBusy && CanEditPersonalSettings);
        ProbeCentralLogPathCommand = new AsyncRelayCommand(ProbeCentralLogPathAsync, () => !IsBusy && !AccBrowser.IsBusy && CanEditSystemSettings);
        OpenUserGroupsCommand = new RelayCommand(_ => OpenUserGroups(), _ => CanEditSystemSettings && _userGroupsWindowFactory is not null);
        BrowseAccFolderCommand = AccBrowser.BrowseFolderCommand;
        BrowseAccParentFolderCommand = AccBrowser.BrowseParentFolderCommand;
        OpenSelectedAccFolderCommand = AccBrowser.OpenSelectedFolderCommand;
        UseSelectedAccFileCommand = AccBrowser.UseSelectedFileCommand;
        ResolveAccDocumentCommand = AccBrowser.ResolveDocumentCommand;
        CopyAccResolvedDocsUrlCommand = AccBrowser.CopyResolvedDocsUrlCommand;
        OpenAccResolvedDocsUrlCommand = AccBrowser.OpenResolvedDocsUrlCommand;
    }

    public SettingsSurfaceScope Scope { get; }

    public IReadOnlyList<string> AvailableFonts { get; }
    public IReadOnlyList<string> LogLevels { get; }
    public AccReadOnlyDocumentBrowserViewModel AccBrowser { get; }

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
                AccBrowser.NotifyHostStateChanged();
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
    private bool _autoSyncProjectLabelNames = SystemSettingsDefaults.EmailAutoSyncProjectLabelNames;
    private int _accViewerMaxTabs = 10;
    private int _workflowMaxOpenChildInstances = SystemSettingsDefaults.WorkflowMaxOpenChildInstances;
    private bool _pilotEnabled = SystemSettingsDefaults.PilotEnabled;
    private string _pilotAllowedUserIds = SystemSettingsDefaults.PilotAllowedUserIds;
    private string _pilotAllowedWorkflowCodes = SystemSettingsDefaults.PilotAllowedWorkflowCodes;
    private string _accServiceBaseUrl = string.Empty;
    private string _accServicePinnedCertificateThumbprints = string.Empty;
    private string _accBootstrapAdminEmail = string.Empty;
    private string _accProjectTemplateName = string.Empty;
    private string _accManualUploadAllowedExtensions = SystemSettingsDefaults.AccManualUploadAllowedExtensions;
    private string _projectWorkScanExclusionRules = SystemSettingsDefaults.ProjectWorkScanExclusionRules;
    private string _crashReportSharePath = SystemSettingsDefaults.DiagnosticsCrashReportSharePath;
    private string _crashAppFilters = SystemSettingsDefaults.DiagnosticsCrashAppFilters;
    private int _crashLookbackDays = SystemSettingsDefaults.DiagnosticsCrashLookbackDays;
    private int _crashReportRetentionDays = SystemSettingsDefaults.DiagnosticsCrashReportRetentionDays;
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

    public bool AutoSyncProjectLabelNames
    {
        get => _autoSyncProjectLabelNames;
        set => SetField(ref _autoSyncProjectLabelNames, value);
    }

    public int AccViewerMaxTabs
    {
        get => _accViewerMaxTabs;
        set => SetField(ref _accViewerMaxTabs, value);
    }

    public int WorkflowMaxOpenChildInstances
    {
        get => _workflowMaxOpenChildInstances;
        set => SetField(ref _workflowMaxOpenChildInstances, value);
    }

    public string AccServiceBaseUrl
    {
        get => _accServiceBaseUrl;
        set => SetField(ref _accServiceBaseUrl, value);
    }

    public string AccServicePinnedCertificateThumbprints
    {
        get => _accServicePinnedCertificateThumbprints;
        set => SetField(ref _accServicePinnedCertificateThumbprints, value);
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

    public string ProjectWorkScanExclusionRules
    {
        get => _projectWorkScanExclusionRules;
        set => SetField(ref _projectWorkScanExclusionRules, value);
    }

    /// <summary>Empty falls back to <c>{CentralLogPath}\CrashReports</c> (DEV-010).</summary>
    public string CrashReportSharePath
    {
        get => _crashReportSharePath;
        set => SetField(ref _crashReportSharePath, value);
    }

    public string CrashAppFilters
    {
        get => _crashAppFilters;
        set => SetField(ref _crashAppFilters, value);
    }

    public int CrashLookbackDays
    {
        get => _crashLookbackDays;
        set => SetField(ref _crashLookbackDays, value);
    }

    public int CrashReportRetentionDays
    {
        get => _crashReportRetentionDays;
        set => SetField(ref _crashReportRetentionDays, value);
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

    public string AccServiceRuntimeProjectsSummary
    {
        get => _accServiceRuntimeProjectsSummary;
        private set => SetField(ref _accServiceRuntimeProjectsSummary, value);
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

    public ObservableCollection<string> AccKnownProjectIds => AccBrowser.KnownProjectIds;

    public string? SelectedAccKnownProjectId
    {
        get => AccBrowser.SelectedKnownProjectId;
        set
        {
            if (!string.Equals(AccBrowser.SelectedKnownProjectId, value, StringComparison.Ordinal))
            {
                AccBrowser.SelectedKnownProjectId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AccLookupProjectId));
                OnPropertyChanged(nameof(AccLookupFolderId));
                OnPropertyChanged(nameof(AccLookupFileName));
                OnPropertyChanged(nameof(AccBrowseSummary));
                OnPropertyChanged(nameof(AccBrowseTrailText));
            }
        }
    }

    public string AccLookupProjectId
    {
        get => AccBrowser.LookupProjectId;
        set
        {
            if (!string.Equals(AccBrowser.LookupProjectId, value, StringComparison.Ordinal))
            {
                AccBrowser.LookupProjectId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedAccKnownProjectId));
            }
        }
    }

    public string AccLookupFolderId
    {
        get => AccBrowser.LookupFolderId;
        set
        {
            if (!string.Equals(AccBrowser.LookupFolderId, value, StringComparison.Ordinal))
            {
                AccBrowser.LookupFolderId = value;
                OnPropertyChanged();
            }
        }
    }

    public string AccLookupFileName
    {
        get => AccBrowser.LookupFileName;
        set
        {
            if (!string.Equals(AccBrowser.LookupFileName, value, StringComparison.Ordinal))
            {
                AccBrowser.LookupFileName = value;
                OnPropertyChanged();
            }
        }
    }

    public string AccLookupResultSummary => AccBrowser.LookupResultSummary;

    public string AccLookupResolvedDocsUrl => AccBrowser.LookupResolvedDocsUrl;

    public string AccBrowseSummary => AccBrowser.BrowseSummary;

    public string AccBrowseTrailText => AccBrowser.BrowseTrailText;

    public ObservableCollection<AccFolderBrowseEntry> AccBrowseFolders => AccBrowser.BrowseFolders;

    public ObservableCollection<AccFolderBrowseEntry> AccBrowseFiles => AccBrowser.BrowseFiles;

    public AccFolderBrowseEntry? SelectedAccBrowseFolder
    {
        get => AccBrowser.SelectedBrowseFolder;
        set
        {
            if (!Equals(AccBrowser.SelectedBrowseFolder, value))
            {
                AccBrowser.SelectedBrowseFolder = value;
                OnPropertyChanged();
            }
        }
    }

    public AccFolderBrowseEntry? SelectedAccBrowseFile
    {
        get => AccBrowser.SelectedBrowseFile;
        set
        {
            if (!Equals(AccBrowser.SelectedBrowseFile, value))
            {
                AccBrowser.SelectedBrowseFile = value;
                OnPropertyChanged();
            }
        }
    }

    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseLogDirectoryCommand { get; }
    public AsyncRelayCommand ProbeCentralLogPathCommand { get; }
    public RelayCommand OpenUserGroupsCommand { get; }
    public AsyncRelayCommand BrowseAccFolderCommand { get; }
    public AsyncRelayCommand BrowseAccParentFolderCommand { get; }
    public AsyncRelayCommand OpenSelectedAccFolderCommand { get; }
    public RelayCommand UseSelectedAccFileCommand { get; }
    public AsyncRelayCommand ResolveAccDocumentCommand { get; }
    public RelayCommand CopyAccResolvedDocsUrlCommand { get; }
    public RelayCommand OpenAccResolvedDocsUrlCommand { get; }

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
            AppErrorReporter.Report(ex, "טעינת הגדרות");
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
        // Must stay on the UI context: everything below raises PropertyChanged and CanExecuteChanged
        // on WPF-bound state. ConfigureAwait(false) here threw "a different thread owns it" whenever
        // the authorization query actually went async instead of returning a cached result.
        var isAdmin = await _authorization
            .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.SystemSettingsWrite, cancellationToken)
            .ConfigureAwait(true);

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
        OpenUserGroupsCommand.RaiseCanExecuteChanged();
        AccBrowser.NotifyHostStateChanged();
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
                var systemDto = BuildSystemDto();
                await _systemCommand.SaveSystemSettingsAsync(systemDto).ConfigureAwait(true);
                _scanExclusionPolicy?.ReplaceRules(systemDto.ProjectWork.ScanExclusionRules);
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
            AppErrorReporter.Report(ex, "שמירת הגדרות");
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

        if (WorkflowMaxOpenChildInstances <= 0)
        {
            error = "מכסת מופעי תת-תהליך פתוחים חייבת להיות מספר חיובי.";
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
            AccViewerMaxTabs,
            AutoSyncProjectLabelNames),
        new AccSystemSettingsDto(
            NormalizeAccServiceBaseUrl(AccServiceBaseUrl),
            NormalizePinnedCertificateThumbprints(AccServicePinnedCertificateThumbprints),
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
            !string.IsNullOrWhiteSpace(CentralLogPath)),
        new WorkflowSystemSettingsDto(
            Math.Max(1, WorkflowMaxOpenChildInstances),
            _pilotEnabled,
            _pilotAllowedUserIds ?? string.Empty,
            _pilotAllowedWorkflowCodes ?? string.Empty),
        new ProjectWorkSystemSettingsDto(
            string.IsNullOrWhiteSpace(ProjectWorkScanExclusionRules)
                ? SystemSettingsDefaults.ProjectWorkScanExclusionRules
                : ProjectWorkScanExclusionRules.Trim()),
        new DiagnosticsSystemSettingsDto(
            CrashReportSharePath?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(CrashAppFilters)
                ? SystemSettingsDefaults.DiagnosticsCrashAppFilters
                : CrashAppFilters.Trim(),
            Math.Max(1, CrashLookbackDays),
            Math.Max(1, CrashReportRetentionDays)));

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
        AutoSyncProjectLabelNames = system.EmailOffice.AutoSyncProjectLabelNames;
        AccViewerMaxTabs = system.EmailOffice.AccViewerMaxTabs;
        WorkflowMaxOpenChildInstances = system.Workflow.MaxOpenChildInstances;
        _pilotEnabled = system.Workflow.PilotEnabled;
        _pilotAllowedUserIds = system.Workflow.PilotAllowedUserIds ?? string.Empty;
        _pilotAllowedWorkflowCodes = system.Workflow.PilotAllowedWorkflowCodes ?? string.Empty;
        AccServiceBaseUrl = system.Acc.AccServiceBaseUrl;
        AccServicePinnedCertificateThumbprints = system.Acc.AccServicePinnedCertificateThumbprints;
        AccBootstrapAdminEmail = system.Acc.AccBootstrapAdminEmail;
        AccProjectTemplateName = system.Acc.AccProjectTemplateName;
        AccManualUploadAllowedExtensions = system.Acc.AccManualUploadAllowedExtensions;
        ProjectWorkScanExclusionRules = system.ProjectWork.ScanExclusionRules;
        CrashReportSharePath = system.Diagnostics.CrashReportSharePath;
        CrashAppFilters = system.Diagnostics.CrashAppFilters;
        CrashLookbackDays = system.Diagnostics.CrashLookbackDays;
        CrashReportRetentionDays = system.Diagnostics.CrashReportRetentionDays;
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

    private void OpenUserGroups()
    {
        if (_userGroupsWindowFactory is null || !CanEditSystemSettings)
            return;

        try
        {
            ThemeResourceLoader.EnsureApplicationResourcesMerged();
            var window = _userGroupsWindowFactory.Create();
            var owner = System.Windows.Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive);
            if (owner is not null)
                window.Owner = owner;
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            SummaryMessage = $"פתיחת ניהול קבוצות נכשלה: {ex.Message}";
            MessageBox.Show(SummaryMessage, "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        var presentation = await _accControlPlaneStatusPresenter
            .BuildAsync(AccControlPlaneStatusPresentationKind.SettingsRuntime)
            .ConfigureAwait(true);

        AccServiceRuntimeHint = presentation.Hint ?? AccServiceRuntimeHint;
        AccServiceRuntimeModeSummary = presentation.ModeSummary;
        AccServiceRuntimeKeySummary = presentation.KeySummary;
        AccServiceRuntimeProjectsSummary = presentation.ProjectsSummary;
        await LoadAccBrowserProjectsAsync(presentation.KnownProjectIds).ConfigureAwait(true);
        AccServiceRuntimeHealthSummary = presentation.HealthSummary;
        AccServiceRuntimeDiagnosticsSummary = presentation.DiagnosticsSummary;
    }

    private async Task LoadAccBrowserProjectsAsync(IReadOnlyList<string> fallbackProjectIds)
    {
        try
        {
            var projects = await _accProjectCatalogService.GetProjectsAsync().ConfigureAwait(true);
            if (projects.Count > 0)
            {
                AccBrowser.LoadKnownProjects(projects);
                return;
            }
        }
        catch
        {
            // Keep the settings surface usable even if the richer catalog lookup fails.
        }

        AccBrowser.LoadKnownProjectIds(fallbackProjectIds);
    }

    public async Task BrowseAccFolderAsync()
        => await AccBrowser.BrowseFolderAsync().ConfigureAwait(true);

    public async Task OpenSelectedAccFolderAsync()
        => await AccBrowser.OpenSelectedFolderAsync().ConfigureAwait(true);

    public async Task BrowseAccParentFolderAsync()
        => await AccBrowser.BrowseParentFolderAsync().ConfigureAwait(true);

    public async Task ResolveAccDocumentAsync()
        => await AccBrowser.ResolveDocumentAsync().ConfigureAwait(true);

    private static string NormalizeAccServiceBaseUrl(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed.TrimEnd('/');
    }

    private static string NormalizePinnedCertificateThumbprints(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var pins = value
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pin => pin.Replace(" ", string.Empty, StringComparison.Ordinal))
            .Where(pin => pin.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(';', pins);
    }

    private static LogLevelDto ParseLevel(string value)
        => Enum.TryParse<LogLevelDto>(value, ignoreCase: true, out var level) ? level : LogLevelDto.Error;

    private void ForwardAccBrowserPropertyChanged(string? propertyName)
    {
        if (propertyName == nameof(AccReadOnlyDocumentBrowserViewModel.IsBusy))
        {
            SaveCommand.RaiseCanExecuteChanged();
            ReloadCommand.RaiseCanExecuteChanged();
            BrowseLogDirectoryCommand.RaiseCanExecuteChanged();
            ProbeCentralLogPathCommand.RaiseCanExecuteChanged();
            return;
        }

        switch (propertyName)
        {
            case nameof(AccReadOnlyDocumentBrowserViewModel.SelectedKnownProjectId):
                OnPropertyChanged(nameof(SelectedAccKnownProjectId));
                break;
            case nameof(AccReadOnlyDocumentBrowserViewModel.LookupProjectId):
                OnPropertyChanged(nameof(AccLookupProjectId));
                break;
            case nameof(AccReadOnlyDocumentBrowserViewModel.LookupFolderId):
                OnPropertyChanged(nameof(AccLookupFolderId));
                break;
            case nameof(AccReadOnlyDocumentBrowserViewModel.LookupFileName):
                OnPropertyChanged(nameof(AccLookupFileName));
                break;
            case nameof(AccReadOnlyDocumentBrowserViewModel.LookupResultSummary):
                OnPropertyChanged(nameof(AccLookupResultSummary));
                break;
            case nameof(AccReadOnlyDocumentBrowserViewModel.LookupResolvedDocsUrl):
                OnPropertyChanged(nameof(AccLookupResolvedDocsUrl));
                break;
            case nameof(AccReadOnlyDocumentBrowserViewModel.BrowseSummary):
                OnPropertyChanged(nameof(AccBrowseSummary));
                break;
            case nameof(AccReadOnlyDocumentBrowserViewModel.BrowseTrailText):
                OnPropertyChanged(nameof(AccBrowseTrailText));
                break;
            case nameof(AccReadOnlyDocumentBrowserViewModel.SelectedBrowseFolder):
                OnPropertyChanged(nameof(SelectedAccBrowseFolder));
                break;
            case nameof(AccReadOnlyDocumentBrowserViewModel.SelectedBrowseFile):
                OnPropertyChanged(nameof(SelectedAccBrowseFile));
                break;
        }
    }

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
