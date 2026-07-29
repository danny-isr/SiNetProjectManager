using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public sealed class R03ReportViewModel : ObservableObject
{
    private readonly IMasterPlanR03ReportService _service;
    private readonly ICurrentUserProfileService _profileService;
    private readonly AsyncRelayCommand _generateCommand;
    private readonly AsyncRelayCommand _previewCommand;
    private readonly RelayCommand _clearEmployeesCommand;
    private readonly RelayCommand _selectAllEmployeesCommand;
    private readonly List<SelectableR03Employee> _allEmployees = [];
    private readonly HashSet<int> _selectedEmployeeIds = [];

    private string _statusMessage = "בחר חודש ושנה — הצג נתונים בטבלה או הפק ל-Google Sheets.";
    private string _viewSummary = string.Empty;
    private string _employeeSearchText = string.Empty;
    private string _currentUserEmployeeText = string.Empty;
    private bool _isBusy;
    private bool _hasViewData;
    private bool _isAdminMode;
    private bool _isEmployeePopupOpen;
    private bool _initialized;
    private int _year = DateTime.Today.Year;
    private int _month = DateTime.Today.Month;
    private string? _resultUrl;
    private int? _selfEmployeeId;

    public R03ReportViewModel(
        IMasterPlanR03ReportService service,
        ICurrentUserProfileService profileService)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        Years = new ObservableCollection<int>(Enumerable.Range(DateTime.Today.Year - 1, 3));
        Months = new ObservableCollection<int>(Enumerable.Range(1, 12));
        ViewDailyRows = new ObservableCollection<R03DailyPreviewRow>();
        FilteredEmployees = new ObservableCollection<SelectableR03Employee>();
        _generateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy && IsAdminMode);
        _previewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy);
        _clearEmployeesCommand = new RelayCommand(_ => ClearEmployeeSelection(), _ => IsAdminMode && !IsBusy);
        _selectAllEmployeesCommand = new RelayCommand(_ => SelectAllEmployees(), _ => IsAdminMode && !IsBusy);
    }

    public ObservableCollection<int> Years { get; }
    public ObservableCollection<int> Months { get; }
    public ObservableCollection<R03DailyPreviewRow> ViewDailyRows { get; }
    public ObservableCollection<SelectableR03Employee> FilteredEmployees { get; }

    public ICommand GenerateCommand => _generateCommand;
    public ICommand PreviewCommand => _previewCommand;
    public ICommand ClearEmployeeSelectionCommand => _clearEmployeesCommand;
    public ICommand SelectAllEmployeesCommand => _selectAllEmployeesCommand;

    public int Year
    {
        get => _year;
        set => SetField(ref _year, value);
    }

    public int Month
    {
        get => _month;
        set => SetField(ref _month, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string ViewSummary
    {
        get => _viewSummary;
        private set => SetField(ref _viewSummary, value);
    }

    public bool HasViewData
    {
        get => _hasViewData;
        private set => SetField(ref _hasViewData, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                _generateCommand.RaiseCanExecuteChanged();
                _previewCommand.RaiseCanExecuteChanged();
                _clearEmployeesCommand.RaiseCanExecuteChanged();
                _selectAllEmployeesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsAdminMode
    {
        get => _isAdminMode;
        private set
        {
            if (SetField(ref _isAdminMode, value))
            {
                OnPropertyChanged(nameof(IsNotAdminMode));
                _generateCommand.RaiseCanExecuteChanged();
                _clearEmployeesCommand.RaiseCanExecuteChanged();
                _selectAllEmployeesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotAdminMode => !IsAdminMode;

    public string CurrentUserEmployeeText
    {
        get => _currentUserEmployeeText;
        private set => SetField(ref _currentUserEmployeeText, value);
    }

    public string EmployeeSearchText
    {
        get => _employeeSearchText;
        set
        {
            if (SetField(ref _employeeSearchText, value))
                ApplyEmployeeFilter();
        }
    }

    public bool IsEmployeePopupOpen
    {
        get => _isEmployeePopupOpen;
        set => SetField(ref _isEmployeePopupOpen, value);
    }

    public string SelectedEmployeesSummary
    {
        get
        {
            if (_selectedEmployeeIds.Count == 0)
                return "כל העובדים";
            if (_selectedEmployeeIds.Count == 1)
            {
                var name = _allEmployees.FirstOrDefault(e => e.Id == _selectedEmployeeIds.First())?.Name;
                return string.IsNullOrWhiteSpace(name) ? "עובד אחד" : name!;
            }

            return $"{_selectedEmployeeIds.Count} עובדים נבחרו";
        }
    }

    public string? ResultUrl
    {
        get => _resultUrl;
        private set => SetField(ref _resultUrl, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;
        _initialized = true;
        await LoadEmployeesAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadEmployeesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _profileService.GetCurrentUserAsync(cancellationToken).ConfigureAwait(true);
            if (profile is null)
            {
                StatusMessage = "אין משתמש מחובר.";
                return;
            }

            IsAdminMode = AppFeatureAuthorization.SatisfiesRole(profile.Role, AppRole.Management);
            _selfEmployeeId = profile.MasterPlanEmployeeId;

            if (IsAdminMode)
            {
                var employees = await _service.GetEmployeesAsync(activeOnly: true, cancellationToken)
                    .ConfigureAwait(true);
                ReplaceEmployeeList(employees);
                CurrentUserEmployeeText = string.Empty;
            }
            else if (profile.MasterPlanEmployeeId is int selfId)
            {
                var self = new R03EmployeeInfo(selfId, profile.DisplayName);
                ReplaceEmployeeList([self]);
                var match = _allEmployees.FirstOrDefault();
                if (match is not null)
                    match.IsSelected = true;
                CurrentUserEmployeeText = $"עובד: {profile.DisplayName}";
            }
            else
            {
                ReplaceEmployeeList([]);
                CurrentUserEmployeeText = "לא מקושר עובד MasterPlan למשתמש הנוכחי.";
                StatusMessage = CurrentUserEmployeeText;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "שגיאה בטעינת עובדים: " + ex.Message;
        }
    }

    private void ReplaceEmployeeList(IReadOnlyList<R03EmployeeInfo> employees)
    {
        foreach (var emp in _allEmployees)
            emp.SelectionChanged -= OnEmployeeSelectionChanged;

        _allEmployees.Clear();
        _selectedEmployeeIds.Clear();

        foreach (var e in employees.OrderBy(x => x.EmployeeName, StringComparer.CurrentCulture))
        {
            var selectable = new SelectableR03Employee(e);
            selectable.SelectionChanged += OnEmployeeSelectionChanged;
            _allEmployees.Add(selectable);
        }

        ApplyEmployeeFilter();
        OnPropertyChanged(nameof(SelectedEmployeesSummary));
    }

    private void OnEmployeeSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is not SelectableR03Employee employee)
            return;

        if (employee.IsSelected)
            _selectedEmployeeIds.Add(employee.Id);
        else
            _selectedEmployeeIds.Remove(employee.Id);

        OnPropertyChanged(nameof(SelectedEmployeesSummary));
    }

    private void ApplyEmployeeFilter()
    {
        var search = EmployeeSearchText?.Trim() ?? string.Empty;
        FilteredEmployees.Clear();
        var filtered = string.IsNullOrEmpty(search)
            ? _allEmployees
            : _allEmployees.Where(e => e.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        foreach (var e in filtered)
            FilteredEmployees.Add(e);
    }

    private void ClearEmployeeSelection()
    {
        foreach (var emp in _allEmployees)
            emp.IsSelected = false;
        _selectedEmployeeIds.Clear();
        EmployeeSearchText = string.Empty;
        OnPropertyChanged(nameof(SelectedEmployeesSummary));
    }

    private void SelectAllEmployees()
    {
        foreach (var emp in _allEmployees)
            emp.IsSelected = true;
        OnPropertyChanged(nameof(SelectedEmployeesSummary));
    }

    private R03ReportRequest BuildRequest()
    {
        if (!IsAdminMode)
        {
            if (_selfEmployeeId is not int selfId)
                throw new InvalidOperationException("לא נמצא זיהוי עובד מקושר למשתמש הנוכחי.");

            return new R03ReportRequest(Year, Month, [selfId]);
        }

        var ids = _selectedEmployeeIds.Count == 0
            ? Array.Empty<int>()
            : _selectedEmployeeIds.ToArray();
        return new R03ReportRequest(Year, Month, ids);
    }

    private async Task PreviewAsync()
    {
        IsBusy = true;
        ResultUrl = null;
        HasViewData = false;
        ViewDailyRows.Clear();
        ViewSummary = string.Empty;
        try
        {
            StatusMessage = "טוען נתונים...";
            var result = await _service.PreviewAsync(BuildRequest()).ConfigureAwait(true);

            if (!result.Success)
            {
                StatusMessage = result.Error ?? "טעינה נכשלה.";
                return;
            }

            foreach (var row in result.Rows)
                ViewDailyRows.Add(row);

            HasViewData = ViewDailyRows.Count > 0;
            ViewSummary =
                $"סה\"כ: נוכחות {result.TotalAttendance:N2} | מדווח {result.TotalReported:N2} | הפרש {result.TotalDifference:N2}";
            StatusMessage = HasViewData
                ? $"הוצגו {ViewDailyRows.Count} שורות."
                : "לא נמצאו נתונים עבור החודש שנבחר.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            MessageBox.Show(ex.Message, "R03", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GenerateAsync()
    {
        if (!IsAdminMode)
        {
            StatusMessage = "הפקת Google Sheets זמינה להנהלה בלבד.";
            return;
        }

        IsBusy = true;
        ResultUrl = null;
        try
        {
            var progress = new Progress<(string Phase, string Message, int Percent)>(p =>
                StatusMessage = $"{p.Percent}% — {p.Message}");

            var result = await _service.GenerateAsync(BuildRequest(), progress).ConfigureAwait(true);

            if (!result.Success)
            {
                StatusMessage = result.Error ?? "הפקה נכשלה.";
                return;
            }

            ResultUrl = result.Url;
            StatusMessage = $"הושלם ({result.RowCount} שורות).";
            if (!string.IsNullOrWhiteSpace(result.Url))
                Process.Start(new ProcessStartInfo(result.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            MessageBox.Show(ex.Message, "R03", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
