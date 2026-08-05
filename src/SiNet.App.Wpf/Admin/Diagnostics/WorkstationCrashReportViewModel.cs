using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Diagnostics;
using SiNet.Application.Settings;

namespace SiNet.App.Wpf.Admin.Diagnostics;

/// <summary>
/// «דוח קריסות תחנה» (DEV-010). Collects the user's reason for the report, reads the local Event Log,
/// and exports a CSV plus a Markdown file meant for AI analysis. Available to any signed-in user.
/// </summary>
public sealed class WorkstationCrashReportViewModel : ObservableObject
{
    private const int DescriptionMaxLength = 1000;

    private readonly IWorkstationEventLogReader _reader;
    private readonly IMachineProfileProvider _machineProfile;
    private readonly IWorkstationCrashReportStore _store;
    private readonly ISystemSettingsQueryService _settings;
    private readonly IAppLogger _logger;

    private CancellationTokenSource? _cancellation;
    private WorkstationCrashReport? _report;
    private string? _lastSavedFolder;

    private CrashReasonCategoryOption? _selectedCategory;
    private string _description = string.Empty;
    private DateTime? _lastOccurrenceDate;
    private string _lastOccurrenceTime = string.Empty;
    private int _lookbackDays = SystemSettingsDefaults.DiagnosticsCrashLookbackDays;
    private string _appFilters = SystemSettingsDefaults.DiagnosticsCrashAppFilters;
    private int _maxEvents = 2000;
    private CrashReportScope _scope = CrashReportScope.Both;
    private bool _isBusy;
    private string _status = "מלא סיבה ותיאור, ואז «הפק דוח».";
    private string _summary = string.Empty;
    private string _machineSummary = string.Empty;
    private bool _hasReport;

    public WorkstationCrashReportViewModel(
        IWorkstationEventLogReader reader,
        IMachineProfileProvider machineProfile,
        IWorkstationCrashReportStore store,
        ISystemSettingsQueryService settings,
        IAppLogger logger)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _machineProfile = machineProfile ?? throw new ArgumentNullException(nameof(machineProfile));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Categories = [.. Enum.GetValues<CrashReasonCategory>().Select(c => new CrashReasonCategoryOption(c))];
        Scopes = [.. Enum.GetValues<CrashReportScope>().Select(s => new CrashReportScopeOption(s))];
        Events = [];

        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => CanGenerate);
        CancelCommand = new RelayCommand(_ => _cancellation?.Cancel(), _ => IsBusy);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => HasReport && !IsBusy);
        ExportMarkdownCommand = new AsyncRelayCommand(ExportMarkdownAsync, () => HasReport && !IsBusy);
        SaveToShareCommand = new AsyncRelayCommand(SaveToShareAsync, () => HasReport && !IsBusy);
        OpenFolderCommand = new RelayCommand(_ => OpenSavedFolder(), _ => _lastSavedFolder is not null);
    }

    public ObservableCollection<CrashReasonCategoryOption> Categories { get; }

    public ObservableCollection<CrashReportScopeOption> Scopes { get; }

    public ObservableCollection<CrashEventRowViewModel> Events { get; }

    public int DescriptionMaxLengthValue => DescriptionMaxLength;

    public CrashReasonCategoryOption? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            var trimmed = value?.Length > DescriptionMaxLength ? value[..DescriptionMaxLength] : value;
            if (SetField(ref _description, trimmed ?? string.Empty))
            {
                RaiseCommandStates();
            }
        }
    }

    public DateTime? LastOccurrenceDate
    {
        get => _lastOccurrenceDate;
        set => SetField(ref _lastOccurrenceDate, value);
    }

    /// <summary>Optional <c>HH:mm</c>. Ignored when empty or unparsable.</summary>
    public string LastOccurrenceTime
    {
        get => _lastOccurrenceTime;
        set => SetField(ref _lastOccurrenceTime, value ?? string.Empty);
    }

    public int LookbackDays
    {
        get => _lookbackDays;
        set => SetField(ref _lookbackDays, value);
    }

    public string AppFilters
    {
        get => _appFilters;
        set => SetField(ref _appFilters, value ?? string.Empty);
    }

    public int MaxEvents
    {
        get => _maxEvents;
        set => SetField(ref _maxEvents, value);
    }

    public CrashReportScope Scope
    {
        get => _scope;
        set => SetField(ref _scope, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool HasReport
    {
        get => _hasReport;
        private set
        {
            if (SetField(ref _hasReport, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetField(ref _summary, value);
    }

    public string MachineSummary
    {
        get => _machineSummary;
        private set => SetField(ref _machineSummary, value);
    }

    public bool CanGenerate
        => !IsBusy && SelectedCategory is not null && !string.IsNullOrWhiteSpace(Description);

    public ICommand GenerateCommand { get; }

    public RelayCommand CancelCommand { get; }

    public ICommand ExportCsvCommand { get; }

    public ICommand ExportMarkdownCommand { get; }

    public ICommand SaveToShareCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    /// <summary>Pulls the admin defaults so the user starts from the office-wide configuration.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var settings = await _settings.GetSystemSettingsAsync().ConfigureAwait(true);
            LookbackDays = settings.Diagnostics.CrashLookbackDays;
            AppFilters = settings.Diagnostics.CrashAppFilters;
        }
        catch (InvalidOperationException ex)
        {
            _logger.Warn($"Crash report defaults unavailable: {ex.Message}");
            Status = "לא ניתן לקרוא הגדרות מערכת — נעשה שימוש בברירות מחדל.";
        }
    }

    private async Task GenerateAsync()
    {
        if (!CanGenerate)
        {
            return;
        }

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        IsBusy = true;
        Status = "אוסף אירועים מיומן האירועים…";

        try
        {
            var generatedAt = DateTimeOffset.Now;
            var query = new WorkstationCrashQuery(
                generatedAt.AddDays(-Math.Max(1, LookbackDays)),
                ParseFilters(AppFilters),
                Scope,
                Math.Max(1, MaxEvents));

            var events = await _reader.ReadAsync(query, token).ConfigureAwait(true);
            Status = "אוסף פרטי מכונה…";
            var machine = await _machineProfile.GetProfileAsync(token).ConfigureAwait(true);

            var context = new CrashReportContextDto(
                SelectedCategory!.Value,
                Description.Trim(),
                BuildLastOccurrence());

            _report = WorkstationCrashReportBuilder.Build(query, context, machine, events, generatedAt);
            ApplyReport(_report);
            HasReport = true;
            Status = $"הדוח מוכן: {_report.Summary.TotalEvents} אירועים.";
        }
        catch (OperationCanceledException)
        {
            Status = "ההפקה בוטלה.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Error("Workstation crash report generation failed.", ex);
            Status = $"שגיאה בהפקת הדוח: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void ApplyReport(WorkstationCrashReport report)
    {
        Events.Clear();
        foreach (var crashEvent in report.Events)
        {
            Events.Add(new CrashEventRowViewModel(crashEvent));
        }

        var summary = report.Summary;
        Summary = string.Join(
            " · ",
            new[]
            {
                $"קריסות תוכנה: {summary.ApplicationCrashCount}",
                $"אירועים קריטיים: {summary.CriticalCount}",
                $"מקושרים: {summary.CorrelatedCount}",
                $"ליום: {summary.CrashesPerDay.ToString("F2", CultureInfo.InvariantCulture)}",
                summary.HasBugCheck ? "מסך כחול: כן" : "מסך כחול: לא",
                summary.HasHardwareEvents ? "אירועי חומרה: כן" : "אירועי חומרה: לא",
                summary.HasUnexpectedShutdown ? "כיבוי לא תקין: כן" : "כיבוי לא תקין: לא",
            });

        var machine = report.Machine;
        var gpu = machine.GraphicsAdapters.FirstOrDefault();
        MachineSummary = string.Join(
            " · ",
            new[]
            {
                machine.MachineName,
                machine.OsCaption,
                machine.CpuName,
                $"{machine.TotalMemoryGb.ToString("F1", CultureInfo.InvariantCulture)} GB RAM",
                gpu is null ? "GPU: לא זוהה" : $"{gpu.Name} ({gpu.DriverVersion ?? "?"})",
            });
    }

    private DateTimeOffset? BuildLastOccurrence()
    {
        if (LastOccurrenceDate is not { } date)
        {
            return null;
        }

        var time = TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(LastOccurrenceTime)
            && TimeSpan.TryParse(LastOccurrenceTime.Trim(), CultureInfo.InvariantCulture, out var parsed))
        {
            time = parsed;
        }

        return new DateTimeOffset(date.Date.Add(time), DateTimeOffset.Now.Offset);
    }

    private static IReadOnlyList<string> ParseFilters(string csv)
        => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private Task ExportCsvAsync()
        => ExportAsync(
            WorkstationCrashReportFormatter.ToCsv,
            CrashReportFileNames.CsvSuffix,
            "קובץ CSV|*.csv");

    private Task ExportMarkdownAsync()
        => ExportAsync(
            WorkstationCrashReportFormatter.ToMarkdown,
            CrashReportFileNames.MarkdownSuffix,
            "קובץ Markdown|*.md");

    private async Task ExportAsync(
        Func<WorkstationCrashReport, string> render,
        string suffix,
        string filter)
    {
        if (_report is not { } report)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = CrashReportFileNames.BuildBaseName(
                report.Machine.MachineName, report.GeneratedAt, report.Context.Category) + suffix,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _store.SaveCopyAsync(dialog.FileName, render(report)).ConfigureAwait(true);
            _lastSavedFolder = Path.GetDirectoryName(dialog.FileName);
            OpenFolderCommand.RaiseCanExecuteChanged();
            Status = $"נשמר: {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("Crash report export failed.", ex);
            Status = $"שמירה נכשלה: {ex.Message}";
        }
    }

    private async Task SaveToShareAsync()
    {
        if (_report is not { } report)
        {
            return;
        }

        IsBusy = true;
        Status = "שומר לתיקייה המשותפת…";

        try
        {
            var result = await _store.SaveToShareAsync(report).ConfigureAwait(true);
            _lastSavedFolder = result.FolderPath;
            OpenFolderCommand.RaiseCanExecuteChanged();

            var cleanup = result.DeletedReportCount > 0
                ? $" נוקו {result.DeletedReportCount} קבצים ישנים."
                : string.Empty;

            Status = $"נשמר ל-{result.FolderPath}.{cleanup}{(result.Warning is null ? string.Empty : " " + result.Warning)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Error("Crash report share save failed.", ex);
            Status = $"שמירה לתיקייה המשותפת נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenSavedFolder()
    {
        if (_lastSavedFolder is not { Length: > 0 } folder)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.Warn($"Could not open crash report folder: {ex.Message}");
            Status = $"פתיחת התיקייה נכשלה: {ex.Message}";
        }
    }

    private void RaiseCommandStates()
    {
        OnPropertyChanged(nameof(CanGenerate));
        (GenerateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ExportCsvCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ExportMarkdownCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SaveToShareCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>Selectable reason with its Hebrew label.</summary>
public sealed record CrashReasonCategoryOption(CrashReasonCategory Value)
{
    public string Display => CrashReasonCategoryDisplay.ToHebrew(Value);
}

/// <summary>Selectable focus with its Hebrew label.</summary>
public sealed record CrashReportScopeOption(CrashReportScope Value)
{
    public string Display => CrashReasonCategoryDisplay.ToHebrew(Value);
}

/// <summary>Grid row over one collected event.</summary>
public sealed class CrashEventRowViewModel(WorkstationCrashEventDto crashEvent)
{
    public string Time => crashEvent.TimeCreated.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string Log => crashEvent.LogName;

    public int EventId => crashEvent.EventId;

    public string Provider => crashEvent.ProviderName;

    public string Severity => CrashReasonCategoryDisplay.ToHebrew(crashEvent.Severity);

    public bool IsCritical => crashEvent.Severity == CrashSeverity.Critical;

    public string? AppName => crashEvent.AppName;

    public string? ModuleName => crashEvent.ModuleName;

    public string? ExceptionCode => crashEvent.ExceptionCode;

    public string? CorrelatedWith => crashEvent.CorrelatedWith;

    public string? Message => crashEvent.Message;
}
