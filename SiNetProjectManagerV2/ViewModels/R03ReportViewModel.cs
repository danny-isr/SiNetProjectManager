using SiNetSQL.MVVM;
using SiNetSQL.Services;
using SiOffice.GoogleConnector.Reports;
using SiOffice.GoogleConnector.Reports.Data;
using SiOffice.GoogleConnector.Reports.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SiNetProjectManagerV2.ViewModels;

/// <summary>
/// ViewModel for the R03 Attendance Comparison Report dialog.
/// Supports month/year selection and employee multi-select.
/// </summary>
public class R03ReportViewModel : INotifyPropertyChanged
{
    private R03ReportService? _reportService;
    private CancellationTokenSource? _cts;

    private readonly List<SelectableR03Employee> _allEmployees = [];
    private readonly HashSet<int> _selectedEmployeeIds = [];
    private bool _isLoadingEmployees;

    #region Constructor

    public R03ReportViewModel()
    {
        GenerateCommand = new RelayCommand<object?>(_ => GenerateReportAsync(), _ => CanGenerate());
        ViewCommand = new RelayCommand<object?>(_ => ViewDataAsync(), _ => CanView());
        CancelCommand = new RelayCommand<object?>(_ => Cancel(), _ => IsGenerating);
        OpenUrlCommand = new RelayCommand<object?>(_ => OpenUrl(), _ => !string.IsNullOrEmpty(ResultUrl));
        ClearEmployeeSelectionCommand = new RelayCommand<object?>(_ => ClearEmployeeSelection());

        FilteredEmployees = new ObservableCollection<SelectableR03Employee>();
        ViewDailyRows = new ObservableCollection<R03DailyViewRow>();

        // Default: current month
        _selectedYear = DateTime.Today.Year;
        _selectedMonth = DateTime.Today.Month;

        // Build year/month options
        var currentYear = DateTime.Today.Year;
        AvailableYears = [currentYear - 1, currentYear, currentYear + 1];
        AvailableMonths = Enumerable.Range(1, 12)
            .Select(m => new MonthOption(m, R03ReportRequest.GetHebrewMonthName(m)))
            .ToList();
    }

    public void Initialize(R03ReportService reportService)
    {
        if (_reportService != null) return;

        _reportService = reportService;
        _ = LoadEmployeesAsync();
    }

    #endregion

    #region Month/Year Selection

    public List<int> AvailableYears { get; }
    public List<MonthOption> AvailableMonths { get; }

    private int _selectedYear;
    public int SelectedYear
    {
        get => _selectedYear;
        set { _selectedYear = value; OnPropertyChanged(); }
    }

    private int _selectedMonth;
    public int SelectedMonth
    {
        get => _selectedMonth;
        set { _selectedMonth = value; OnPropertyChanged(); }
    }

    #endregion

    #region Employee Multi-Select

    public ObservableCollection<SelectableR03Employee> FilteredEmployees { get; }

    private string _employeeSearchText = string.Empty;
    public string EmployeeSearchText
    {
        get => _employeeSearchText;
        set
        {
            if (_employeeSearchText != value)
            {
                _employeeSearchText = value;
                OnPropertyChanged();
                ApplyEmployeeFilter();
            }
        }
    }

    private bool _isEmployeePopupOpen;
    public bool IsEmployeePopupOpen
    {
        get => _isEmployeePopupOpen;
        set { _isEmployeePopupOpen = value; OnPropertyChanged(); }
    }

    public string SelectedEmployeesSummary
    {
        get
        {
            var count = _selectedEmployeeIds.Count;
            if (count == 0) return "-- כל העובדים --";
            if (count == 1)
            {
                var emp = _allEmployees.FirstOrDefault(e => e.IsSelected);
                return emp?.Name ?? "1 עובד נבחר";
            }
            return $"{count} עובדים נבחרו";
        }
    }

    public ICommand ClearEmployeeSelectionCommand { get; }

    private bool _isAdminMode;
    /// <summary>Whether the current user is admin (sees all employees).</summary>
    public bool IsAdminMode
    {
        get => _isAdminMode;
        set { _isAdminMode = value; OnPropertyChanged(); }
    }

    #endregion

    #region Employee Data View

    /// <summary>Daily rows for in-app DataGrid display (employee view).</summary>
    public ObservableCollection<R03DailyViewRow> ViewDailyRows { get; }

    private bool _hasViewData;
    /// <summary>Whether the DataGrid has data to show.</summary>
    public bool HasViewData
    {
        get => _hasViewData;
        set { _hasViewData = value; OnPropertyChanged(); }
    }

    private string _viewSummary = string.Empty;
    /// <summary>Summary text shown below the DataGrid.</summary>
    public string ViewSummary
    {
        get => _viewSummary;
        set { _viewSummary = value; OnPropertyChanged(); }
    }

    #endregion

    #region Progress & Result

    private bool _isGenerating;
    public bool IsGenerating
    {
        get => _isGenerating;
        set { _isGenerating = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditFilters)); }
    }

    public bool CanEditFilters => !IsGenerating;

    private string _progressMessage = string.Empty;
    public string ProgressMessage
    {
        get => _progressMessage;
        set { _progressMessage = value; OnPropertyChanged(); }
    }

    private int _progressPercent;
    public int ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); }
    }

    private bool _hasResult;
    public bool HasResult
    {
        get => _hasResult;
        set { _hasResult = value; OnPropertyChanged(); }
    }

    private bool _resultSuccess;
    public bool ResultSuccess
    {
        get => _resultSuccess;
        set { _resultSuccess = value; OnPropertyChanged(); }
    }

    private string _resultMessage = string.Empty;
    public string ResultMessage
    {
        get => _resultMessage;
        set { _resultMessage = value; OnPropertyChanged(); }
    }

    private string? _resultUrl;
    public string? ResultUrl
    {
        get => _resultUrl;
        set { _resultUrl = value; OnPropertyChanged(); }
    }

    private string _resultDetails = string.Empty;
    public string ResultDetails
    {
        get => _resultDetails;
        set { _resultDetails = value; OnPropertyChanged(); }
    }

    #endregion

    #region Commands

    public ICommand GenerateCommand { get; }
    public ICommand ViewCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenUrlCommand { get; }

    private bool CanGenerate()
    {
        if (IsGenerating) return false;
        if (_reportService == null) return false;
        return IsAdminMode;
    }

    private bool CanView()
    {
        if (IsGenerating) return false;
        if (_reportService == null) return false;
        return true;
    }

    #endregion

    #region Methods

    private async Task LoadEmployeesAsync()
    {
        if (_reportService == null || _isLoadingEmployees) return;

        _isLoadingEmployees = true;
        try
        {
            var ctx = CurrentUserContext.Instance;
            IsAdminMode = ctx.IsManagement;

            var employees = await _reportService.GetEmployeesAsync(activeOnly: true);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var previousSelections = new HashSet<int>(_selectedEmployeeIds);

                foreach (var emp in _allEmployees)
                    emp.SelectionChanged -= OnEmployeeSelectionChanged;

                _allEmployees.Clear();

                foreach (var e in employees.OrderBy(e => e.Name, StringComparer.CurrentCulture))
                {
                    var selectable = new SelectableR03Employee(e);
                    if (previousSelections.Contains(e.Id))
                        selectable.IsSelected = true;

                    selectable.SelectionChanged += OnEmployeeSelectionChanged;
                    _allEmployees.Add(selectable);
                }

                // Non-admin: auto-select current user's MasterPlanEmployeeId
                if (!IsAdminMode && ctx.MasterPlanEmployeeId.HasValue)
                {
                    var match = _allEmployees.FirstOrDefault(e => e.Id == ctx.MasterPlanEmployeeId.Value);
                    if (match != null)
                        match.IsSelected = true;
                }

                _selectedEmployeeIds.Clear();
                foreach (var emp in _allEmployees.Where(e => e.IsSelected))
                    _selectedEmployeeIds.Add(emp.Id);

                ApplyEmployeeFilter();
                OnPropertyChanged(nameof(SelectedEmployeesSummary));
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[R03] LoadEmployeesAsync");
            ResultMessage = $"שגיאה בטעינת עובדים: {ex.Message}";
            HasResult = true;
            ResultSuccess = false;
        }
        finally
        {
            _isLoadingEmployees = false;
        }
    }

    private void OnEmployeeSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is SelectableR03Employee employee)
        {
            if (employee.IsSelected)
                _selectedEmployeeIds.Add(employee.Id);
            else
                _selectedEmployeeIds.Remove(employee.Id);

            OnPropertyChanged(nameof(SelectedEmployeesSummary));
        }
    }

    private void ApplyEmployeeFilter()
    {
        var searchLower = EmployeeSearchText?.ToLower().Trim() ?? string.Empty;

        var isOnUIThread = System.Windows.Application.Current.Dispatcher.CheckAccess();
        if (isOnUIThread)
            ApplyEmployeeFilterCore(searchLower);
        else
            System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyEmployeeFilterCore(searchLower));
    }

    private void ApplyEmployeeFilterCore(string searchLower)
    {
        FilteredEmployees.Clear();

        var filtered = string.IsNullOrEmpty(searchLower)
            ? _allEmployees
            : _allEmployees.Where(e => e.Name?.ToLower().Contains(searchLower) ?? false);

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

    private async void GenerateReportAsync()
    {
        if (_reportService == null) return;

        IsGenerating = true;
        HasResult = false;
        ProgressPercent = 0;
        ProgressMessage = "מתחיל...";

        _cts = new CancellationTokenSource();

        try
        {
            var request = BuildRequest();
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                ResultMessage = string.Join("\n", errors);
                HasResult = true;
                ResultSuccess = false;
                return;
            }

            var result = await _reportService.GenerateAsync(
                request,
                (step, message, percent) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        ProgressMessage = message;
                        ProgressPercent = percent;
                    });
                },
                _cts.Token);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                HasResult = true;
                ResultSuccess = result.Success;

                if (result.Success)
                {
                    ResultMessage = "הדוח נוצר בהצלחה!";
                    ResultUrl = result.Url;
                    ResultDetails = $"שורות: {result.RowCount:N0}";
                }
                else
                {
                    ResultMessage = result.Error ?? "שגיאה לא ידועה";
                    ResultUrl = null;
                    ResultDetails = string.Empty;
                }
            });
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "הפעולה בוטלה";
            HasResult = true;
            ResultSuccess = false;
        }
        catch (Exception ex)
        {
            ResultMessage = $"שגיאה: {ex.Message}";
            HasResult = true;
            ResultSuccess = false;
        }
        finally
        {
            IsGenerating = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private R03ReportRequest BuildRequest()
    {
        var request = new R03ReportRequest
        {
            Year = SelectedYear,
            Month = SelectedMonth
        };

        if (_selectedEmployeeIds.Count > 0)
        {
            foreach (var id in _selectedEmployeeIds)
                request.EmployeeIds.Add(id);
        }

        return request;
    }

    private async void ViewDataAsync()
    {
        if (_reportService == null) return;

        IsGenerating = true;
        HasResult = false;
        HasViewData = false;
        ProgressMessage = "טוען נתונים...";
        ProgressPercent = 30;

        _cts = new CancellationTokenSource();

        try
        {
            var request = BuildRequest();
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                ResultMessage = string.Join("\n", errors);
                HasResult = true;
                ResultSuccess = false;
                return;
            }

            var sheets = await _reportService.GetEmployeeDataAsync(request, _cts.Token);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ViewDailyRows.Clear();

                foreach (var emp in sheets)
                {
                    foreach (var day in emp.Days)
                    {
                        ViewDailyRows.Add(new R03DailyViewRow
                        {
                            EmployeeName = emp.EmployeeName,
                            Date = day.Date,
                            DayName = day.DayName,
                            AttendanceHours = Math.Round(day.AttendanceHours, 2),
                            ReportedHours = Math.Round(day.ReportedHours, 2),
                            Difference = Math.Round(day.Difference, 2)
                        });
                    }
                }

                HasViewData = ViewDailyRows.Count > 0;

                var totalAtt = sheets.Sum(s => s.TotalAttendance);
                var totalRep = sheets.Sum(s => s.TotalReported);
                var totalDiff = totalRep - totalAtt;
                ViewSummary = $"סה\"כ: נוכחות {Math.Round(totalAtt, 2)} | מדווח {Math.Round(totalRep, 2)} | הפרש {Math.Round(totalDiff, 2)}";

                if (!HasViewData)
                {
                    ResultMessage = "לא נמצאו נתונים לחודש שנבחר.";
                    HasResult = true;
                    ResultSuccess = false;
                }
            });
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "הפעולה בוטלה";
            HasResult = true;
            ResultSuccess = false;
        }
        catch (Exception ex)
        {
            ResultMessage = $"שגיאה: {ex.Message}";
            HasResult = true;
            ResultSuccess = false;
        }
        finally
        {
            IsGenerating = false;
            ProgressPercent = 0;
            ProgressMessage = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Cancel()
    {
        _cts?.Cancel();
    }

    private void OpenUrl()
    {
        if (string.IsNullOrEmpty(ResultUrl)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ResultUrl,
                UseShellExecute = true
            });
        }
        catch { }
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    #endregion
}

/// <summary>Flat row for the in-app DataGrid view.</summary>
public class R03DailyViewRow
{
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string DayName { get; set; } = string.Empty;
    public decimal AttendanceHours { get; set; }
    public decimal ReportedHours { get; set; }
    public decimal Difference { get; set; }
    public bool IsNegativeDifference => Difference < 0;
}

/// <summary>Simple month option for ComboBox binding.</summary>
public record MonthOption(int Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>Selectable wrapper for R03 employee info.</summary>
public class SelectableR03Employee : INotifyPropertyChanged
{
    private bool _isSelected;

    public SelectableR03Employee(R03EmployeeInfo info)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
    }

    public R03EmployeeInfo Info { get; }
    public int Id => Info.Id;
    public string Name => Info.Name;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
