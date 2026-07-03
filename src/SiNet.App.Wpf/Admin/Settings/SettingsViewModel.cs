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
    private readonly IStatusColorSettingsService _statusColors;
    private readonly IAuthorizationQueryService _authorization;
    private readonly ICurrentUserContext? _currentUser;

    private UserLoggingSettingsDto _loadedLogging = null!;
    private string _summaryMessage = string.Empty;
    private bool _isBusy;

    public SettingsViewModel(
        IAppSettingsService appSettings,
        ISystemSettingsQueryService systemQuery,
        ISystemSettingsCommandService systemCommand,
        ILoggingSettingsCommandService loggingCommand,
        ILoggingRuntimeApplier loggingRuntime,
        IStatusColorSettingsService statusColors,
        IAuthorizationQueryService authorization,
        ICurrentUserContext? currentUser,
        SettingsSurfaceScope scope)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _systemQuery = systemQuery ?? throw new ArgumentNullException(nameof(systemQuery));
        _systemCommand = systemCommand ?? throw new ArgumentNullException(nameof(systemCommand));
        _loggingCommand = loggingCommand ?? throw new ArgumentNullException(nameof(loggingCommand));
        _loggingRuntime = loggingRuntime ?? throw new ArgumentNullException(nameof(loggingRuntime));
        _statusColors = statusColors ?? throw new ArgumentNullException(nameof(statusColors));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _currentUser = currentUser;
        Scope = scope;

        AvailableFonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToList();
        LogLevels = Enum.GetNames<LogLevelDto>();

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && (CanEditPersonalSettings || CanEditSystemSettings));
        ReloadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        BrowseLogDirectoryCommand = new RelayCommand(_ => BrowseLogDirectory(), _ => CanEditPersonalSettings);
        ProbeCentralLogPathCommand = new AsyncRelayCommand(ProbeCentralLogPathAsync, () => !IsBusy && CanEditSystemSettings);
    }

    public SettingsSurfaceScope Scope { get; }

    public IReadOnlyList<string> AvailableFonts { get; }
    public IReadOnlyList<string> LogLevels { get; }

    public ObservableCollection<UserStatusColorRowViewModel> UserStatusColors { get; } = [];
    public ObservableCollection<GlobalStatusColorRowViewModel> GlobalStatusColors { get; } = [];

    public bool CanViewPersonalSettings { get; private set; }
    public bool CanEditPersonalSettings { get; private set; }
    public bool CanViewSystemSettings { get; private set; }
    public bool CanEditSystemSettings { get; private set; }
    public bool CanViewGlobalStatusColors { get; private set; }
    public bool CanEditGlobalStatusColors { get; private set; }

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

    public string FontFamily { get; set; } = UserAppSettingsDefaults.FontFamily;
    public double FontSize { get; set; } = UserAppSettingsDefaults.FontSize;
    public string ForegroundColor { get; set; } = UserAppSettingsDefaults.ForegroundColor;
    public string BackgroundColor { get; set; } = UserAppSettingsDefaults.BackgroundColor;
    public bool AllowMultipleInstances { get; set; } = UserAppSettingsDefaults.AllowMultipleInstances;
    public bool EnableAuthorizationTestMode { get; set; }
    public bool LoggingEnabled { get; set; }
    public string? LogDirectory { get; set; }
    public string LogDirectoryDisplay =>
        string.IsNullOrWhiteSpace(LogDirectory)
            ? $"(ברירת מחדל: {LoggingSettingsMetadata.AppLoggerDefaultLocalLogDirectory})"
            : LogDirectory;
    public double FloatingActiveOpacity { get; set; } = UserAppSettingsDefaults.FloatingActiveOpacity;
    public double FloatingIdleOpacity { get; set; } = UserAppSettingsDefaults.FloatingIdleOpacity;

    public string DefaultProjectTitle { get; set; } = SystemSettingsDefaults.DefaultProjectTitle;
    public string OfficeManagementProjectId { get; set; } = SystemSettingsDefaults.OfficeManagementProjectId;
    public string HourPriceDefault { get; set; } = SystemSettingsDefaults.HourPriceDefault;
    public string InboxFolderName { get; set; } = SystemSettingsDefaults.InboxFolderNameFallback;
    public string? InboxProjectName { get; set; }
    public int AccViewerMaxTabs { get; set; } = 10;
    public string AccServiceBaseUrl { get; set; } = string.Empty;
    public string AccBootstrapAdminEmail { get; set; } = string.Empty;
    public string AccProjectTemplateName { get; set; } = string.Empty;
    public string AccManualUploadAllowedExtensions { get; set; } = SystemSettingsDefaults.AccManualUploadAllowedExtensions;
    public string InspectionTemplatesFolderId { get; set; } = string.Empty;
    public string InspectionReportsFolderId { get; set; } = string.Empty;
    public string ReportsOutputRoot { get; set; } = string.Empty;
    public string StampTemplatePath { get; set; } = string.Empty;
    public string StatusLabelPassed { get; set; } = SystemSettingsDefaults.StatusLabelPassed;
    public string StatusLabelFailed { get; set; } = SystemSettingsDefaults.StatusLabelFailed;
    public string StatusLabelRecurringFailed { get; set; } = SystemSettingsDefaults.StatusLabelRecurringFailed;
    public string StatusLabelNotApplicable { get; set; } = SystemSettingsDefaults.StatusLabelNotApplicable;
    public string OllamaBaseUrl { get; set; } = SystemSettingsDefaults.OllamaBaseUrl;
    public string OllamaModel { get; set; } = SystemSettingsDefaults.OllamaModel;
    public string AiModelSimple { get; set; } = string.Empty;
    public string AiProviderSimple { get; set; } = string.Empty;
    public string AiModelQualityCheck { get; set; } = string.Empty;
    public string AiProviderQualityCheck { get; set; } = string.Empty;
    public string AiModelWriting { get; set; } = string.Empty;
    public string AiProviderWriting { get; set; } = string.Empty;
    public string AiModelDeepAnalysis { get; set; } = string.Empty;
    public string AiProviderDeepAnalysis { get; set; } = string.Empty;
    public string AiConfiguredCloudModelsCsv { get; set; } = string.Empty;
    public string? CentralLogPath { get; set; }
    public int LocalRetentionDays { get; set; } = 14;
    public int CentralRetentionDays { get; set; } = 90;
    public string ClientFileLevel { get; set; } = LogLevelDto.Error.ToString();
    public string ClientCentralLevel { get; set; } = LogLevelDto.Warning.ToString();
    public string AccServiceFileLevel { get; set; } = LogLevelDto.Information.ToString();
    public string AccServiceCentralLevel { get; set; } = LogLevelDto.Warning.ToString();
    public string SyncEngineFileLevel { get; set; } = LogLevelDto.Information.ToString();
    public string SyncEngineCentralLevel { get; set; } = LogLevelDto.Warning.ToString();

    public string RestartRequiredHint =>
        "הגדרות גלובליות נשמרות ב-DB; consumers שקוראים ב-bootstrap דורשים restart.";

    public string CentralLoggingRestartHint => CentralLoggingSettingsDto.RequiresRestartMessage;

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
                ApplyUserSettings(user);
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

        OnPropertyChanged(nameof(CanViewPersonalSettings));
        OnPropertyChanged(nameof(CanEditPersonalSettings));
        OnPropertyChanged(nameof(CanViewSystemSettings));
        OnPropertyChanged(nameof(CanEditSystemSettings));
        OnPropertyChanged(nameof(CanViewGlobalStatusColors));
        OnPropertyChanged(nameof(CanEditGlobalStatusColors));
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
        foreach (var hex in new[] { ForegroundColor, BackgroundColor })
        {
            if (!IsValidHexColor(hex))
            {
                error = $"צבע לא תקין: {hex}";
                return false;
            }
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
            new UserAppearanceSettingsDto(FontFamily, FontSize, ForegroundColor.Trim(), BackgroundColor.Trim()),
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

    private SystemSettingsDto BuildSystemDto() => new(
        new EmailOfficeSystemSettingsDto(
            DefaultProjectTitle.Trim(),
            OfficeManagementProjectId.Trim(),
            HourPriceDefault.Trim(),
            InboxFolderName.Trim(),
            string.IsNullOrWhiteSpace(InboxProjectName) ? null : InboxProjectName.Trim(),
            AccViewerMaxTabs),
        new AccSystemSettingsDto(
            AccServiceBaseUrl.Trim(),
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
        FontSize = user.Appearance.FontSize;
        ForegroundColor = user.Appearance.ForegroundColor;
        BackgroundColor = user.Appearance.BackgroundColor;
        AllowMultipleInstances = user.Behavior.AllowMultipleInstances;
        EnableAuthorizationTestMode = user.EnableAuthorizationTestMode;
        LoggingEnabled = user.Logging.LoggingEnabled;
        LogDirectory = user.Logging.LogDirectory;
        FloatingActiveOpacity = user.FloatingOpacity.ActiveOpacity;
        FloatingIdleOpacity = user.FloatingOpacity.IdleOpacity;
        OnPropertyChanged(nameof(LogDirectoryDisplay));
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
            OnPropertyChanged(nameof(LogDirectoryDisplay));
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

    private static LogLevelDto ParseLevel(string value)
        => Enum.TryParse<LogLevelDto>(value, ignoreCase: true, out var level) ? level : LogLevelDto.Error;

    private static bool IsValidHexColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var hex = value.Trim();
        if (!hex.StartsWith('#'))
        {
            hex = "#" + hex;
        }

        return hex.Length is 7 or 9 && hex.Skip(1).All(Uri.IsHexDigit);
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
        _colorHex = dto.ColorHex;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public int StatusId { get; }
    public string StatusName { get; }

    private string _colorHex;

    public string ColorHex
    {
        get => _colorHex;
        set => SetField(ref _colorHex, value);
    }

    public AsyncRelayCommand SaveCommand { get; }

    private Task SaveAsync() => _service.SetGlobalDefaultColorAsync(StatusId, ColorHex);
}
