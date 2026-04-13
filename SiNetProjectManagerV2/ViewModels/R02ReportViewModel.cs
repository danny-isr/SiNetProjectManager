using SiNetSQL.MVVM;
using SiNetSQL.Services;
using SiOffice.GoogleConnector.Reports;
using SiOffice.GoogleConnector.Reports.Data;
using SiOffice.GoogleConnector.Reports.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;

namespace SiNetProjectManagerV2.ViewModels;

/// <summary>
/// ViewModel for the R02 Hours Report dialog.
/// Supports multi-select projects with search functionality.
/// </summary>
public class R02ReportViewModel : INotifyPropertyChanged
{
#pragma warning disable CS0649
    private R02ReportService? _reportService;
#pragma warning restore CS0649
    private CancellationTokenSource? _cts;

    // Project multi-select collections
    private readonly List<SelectableProjectInfo> _allProjects = new();
    private readonly HashSet<int> _selectedProjectIds = new();

    // Customer multi-select collections
    private readonly List<SelectableCustomerInfo> _allCustomers = new();
    private readonly HashSet<int> _selectedCustomerIds = new();

    // Employee multi-select collections
    private readonly List<SelectableEmployeeInfo> _allEmployees = new();
    private readonly HashSet<int> _selectedEmployeeIds = new();

    // Guard against concurrent load calls
    private bool _isLoadingProjects;
    private bool _isLoadingCustomers;
    private bool _isLoadingEmployees;

    #region Constructor

    public R02ReportViewModel()
    {
        // Initialize commands
        GenerateCommand = new RelayCommand<object?>(_ => GenerateReportAsync(), _ => CanGenerate());
        CancelCommand = new RelayCommand<object?>(_ => Cancel(), _ => IsGenerating);
        OpenUrlCommand = new RelayCommand<object?>(_ => OpenUrl(), _ => !string.IsNullOrEmpty(ResultUrl));
        ClearProjectSelectionCommand = new RelayCommand<object?>(_ => ClearProjectSelection());
        ClearCustomerSelectionCommand = new RelayCommand<object?>(_ => ClearCustomerSelection());
        ClearEmployeeSelectionCommand = new RelayCommand<object?>(_ => ClearEmployeeSelection());

        // Initialize collections
        FilteredCustomers = new ObservableCollection<SelectableCustomerInfo>();
        FilteredProjects = new ObservableCollection<SelectableProjectInfo>();
        FilteredEmployees = new ObservableCollection<SelectableEmployeeInfo>();

        // Default dates
        _startDate = DateTime.Today.AddMonths(-1);
        _endDate = DateTime.Today;
    }

    public void Initialize(R02ReportService reportService)
    {
        if (_reportService != null) return;

        _reportService = reportService;
        _ = LoadFilterDataAsync();
    }

    #endregion

    #region Bindable Properties

    // --- Date Range ---

    private DateTime _startDate;
    public DateTime StartDate
    {
        get => _startDate;
        set { _startDate = value; OnPropertyChanged(); }
    }

    private DateTime _endDate;
    public DateTime EndDate
    {
        get => _endDate;
        set { _endDate = value; OnPropertyChanged(); }
    }

    // --- Customer Multi-Select ---

    public ObservableCollection<SelectableCustomerInfo> FilteredCustomers { get; }

    private string _customerSearchText = string.Empty;
    public string CustomerSearchText
    {
        get => _customerSearchText;
        set
        {
            if (_customerSearchText != value)
            {
                _customerSearchText = value;
                OnPropertyChanged();
                ApplyCustomerFilter();
            }
        }
    }

    private bool _isCustomerPopupOpen;
    public bool IsCustomerPopupOpen
    {
        get => _isCustomerPopupOpen;
        set { _isCustomerPopupOpen = value; OnPropertyChanged(); }
    }

    public string SelectedCustomersSummary
    {
        get
        {
            var count = _selectedCustomerIds.Count;
            if (count == 0)
                return "-- כל הלקוחות --";
            if (count == 1)
            {
                var cust = _allCustomers.FirstOrDefault(c => c.IsSelected);
                return cust?.Name ?? "1 לקוח נבחר";
            }
            return $"{count} לקוחות נבחרו";
        }
    }

    public ICommand ClearCustomerSelectionCommand { get; }

    // --- Project Multi-Select ---

    /// <summary>
    /// Filtered projects based on search text (for display in the popup).
    /// </summary>
    public ObservableCollection<SelectableProjectInfo> FilteredProjects { get; }

    private string _projectSearchText = string.Empty;
    /// <summary>
    /// Search text for filtering projects.
    /// </summary>
    public string ProjectSearchText
    {
        get => _projectSearchText;
        set
        {
            if (_projectSearchText != value)
            {
                _projectSearchText = value;
                OnPropertyChanged();
                ApplyProjectFilter();
            }
        }
    }

    private bool _isProjectPopupOpen;
    /// <summary>
    /// Whether the project selection popup is open.
    /// </summary>
    public bool IsProjectPopupOpen
    {
        get => _isProjectPopupOpen;
        set { _isProjectPopupOpen = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Summary text showing selected projects count.
    /// </summary>
    public string SelectedProjectsSummary
    {
        get
        {
            var count = _selectedProjectIds.Count;
            if (count == 0)
                return "-- כל הפרויקטים --";
            if (count == 1)
            {
                var proj = _allProjects.FirstOrDefault(p => p.IsSelected);
                return proj != null ? $"{proj.ProjectNum} - {proj.Name}" : "1 פרויקט נבחר";
            }
            return $"{count} פרויקטים נבחרו";
        }
    }

    /// <summary>
    /// Command to clear project selection.
    /// </summary>
    public ICommand ClearProjectSelectionCommand { get; }

    private bool _activeProjectsOnly = false;
    public bool ActiveProjectsOnly
    {
        get => _activeProjectsOnly;
        set
        {
            _activeProjectsOnly = value;
            OnPropertyChanged();
            _ = LoadProjectsAsync();
        }
    }

    // --- Employee Multi-Select ---

    public ObservableCollection<SelectableEmployeeInfo> FilteredEmployees { get; }

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
            if (count == 0)
                return "-- כל העובדים --";
            if (count == 1)
            {
                var emp = _allEmployees.FirstOrDefault(e => e.IsSelected);
                return emp?.FullName ?? "1 עובד נבחר";
            }
            return $"{count} עובדים נבחרו";
        }
    }

    public ICommand ClearEmployeeSelectionCommand { get; }

    private bool _activeEmployeesOnly = true;
    public bool ActiveEmployeesOnly
    {
        get => _activeEmployeesOnly;
        set
        {
            _activeEmployeesOnly = value;
            OnPropertyChanged();
            _ = LoadEmployeesAsync();
        }
    }

    // --- Options ---

    private bool _excludeZeroHours = true;
    public bool ExcludeZeroHours
    {
        get => _excludeZeroHours;
        set { _excludeZeroHours = value; OnPropertyChanged(); }
    }

    private bool _isClientExport = false;
    public bool IsClientExport
    {
        get => _isClientExport;
        set { _isClientExport = value; OnPropertyChanged(); }
    }

    // --- Progress ---

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

    // --- Result ---

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
    public ICommand CancelCommand { get; }
    public ICommand OpenUrlCommand { get; }

    private bool CanGenerate()
    {
        if (IsGenerating) return false;
        if (_reportService == null) return false;
        return true;
    }

    #endregion

    #region Methods

    private async Task LoadFilterDataAsync()
    {
        if (_reportService == null) return;

        try
        {
            await LoadCustomersAsync();
            await LoadProjectsAsync();
            await LoadEmployeesAsync();
        }
        catch (Exception ex)
        {
            ResultMessage = $"שגיאה בטעינת נתונים: {ex.Message}";
            HasResult = true;
            ResultSuccess = false;
        }
    }

    private async Task LoadCustomersAsync()
    {
        if (_reportService == null || _isLoadingCustomers) return;

        _isLoadingCustomers = true;
        try
        {
            var customers = await _reportService.GetCustomersAsync();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var previousSelections = new HashSet<int>(_selectedCustomerIds);

                foreach (var cust in _allCustomers)
                {
                    cust.SelectionChanged -= OnCustomerSelectionChanged;
                }

                _allCustomers.Clear();

                // Sort customers alphabetically (A-Z)
                var sortedCustomers = customers.OrderBy(c => c.Name, StringComparer.CurrentCulture);

                foreach (var c in sortedCustomers)
                {
                    var selectable = new SelectableCustomerInfo(c);

                    if (previousSelections.Contains(c.Id))
                    {
                        selectable.IsSelected = true;
                    }

                    selectable.SelectionChanged += OnCustomerSelectionChanged;
                    _allCustomers.Add(selectable);
                }

                _selectedCustomerIds.Clear();
                foreach (var cust in _allCustomers.Where(c => c.IsSelected))
                {
                    _selectedCustomerIds.Add(cust.Id);
                }

                ApplyCustomerFilter();
                OnPropertyChanged(nameof(SelectedCustomersSummary));
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[R02] LoadCustomersAsync");
        }
        finally
        {
            _isLoadingCustomers = false;
        }
    }

    private void OnCustomerSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is SelectableCustomerInfo customer)
        {
            if (customer.IsSelected)
            {
                _selectedCustomerIds.Add(customer.Id);
            }
            else
            {
                _selectedCustomerIds.Remove(customer.Id);
            }
            OnPropertyChanged(nameof(SelectedCustomersSummary));
            // Reload projects when customer selection changes
            _ = LoadProjectsAsync();
        }
    }

    private void ApplyCustomerFilter()
    {
        var searchLower = CustomerSearchText?.ToLower().Trim() ?? string.Empty;

        var isOnUIThread = System.Windows.Application.Current.Dispatcher.CheckAccess();
        if (isOnUIThread)
        {
            ApplyCustomerFilterCore(searchLower);
        }
        else
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyCustomerFilterCore(searchLower));
        }
    }

    private void ApplyCustomerFilterCore(string searchLower)
    {
        FilteredCustomers.Clear();

        var filtered = string.IsNullOrEmpty(searchLower)
            ? _allCustomers
            : _allCustomers.Where(c =>
                (c.Name?.ToLower().Contains(searchLower) ?? false));

        foreach (var c in filtered)
        {
            FilteredCustomers.Add(c);
        }
    }

    private void ClearCustomerSelection()
    {
        foreach (var customer in _allCustomers)
        {
            customer.IsSelected = false;
        }
        _selectedCustomerIds.Clear();
        CustomerSearchText = string.Empty;
        OnPropertyChanged(nameof(SelectedCustomersSummary));
        _ = LoadProjectsAsync();
    }

    private async Task LoadProjectsAsync()
    {
        if (_reportService == null || _isLoadingProjects) return;

        _isLoadingProjects = true;
        try
        {
            // Filter by selected customers (if any)
            int? customerId = _selectedCustomerIds.Count == 1 
                ? _selectedCustomerIds.First() 
                : null;

            var projects = await _reportService.GetProjectsAsync(customerId, ActiveProjectsOnly);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Store previous selections
                var previousSelections = new HashSet<int>(_selectedProjectIds);

                // Unsubscribe old event handlers to prevent memory leaks
                foreach (var proj in _allProjects)
                {
                    proj.SelectionChanged -= OnProjectSelectionChanged;
                }

                // Clear and rebuild the master list
                _allProjects.Clear();

                // Sort projects by ProjectNum numerically (descending - highest first)
                // Non-numeric project numbers go to the end
                var sortedProjects = projects.OrderByDescending(p => 
                    int.TryParse(p.ProjectNum, out var num) ? num : int.MinValue);

                foreach (var p in sortedProjects)
                {
                    var selectable = new SelectableProjectInfo(p);

                    // Restore selection if previously selected
                    if (previousSelections.Contains(p.Id))
                    {
                        selectable.IsSelected = true;
                    }

                    // Subscribe to selection changes
                    selectable.SelectionChanged += OnProjectSelectionChanged;

                    _allProjects.Add(selectable);
                }

                // Update selected IDs to only include projects that still exist
                _selectedProjectIds.Clear();
                foreach (var proj in _allProjects.Where(p => p.IsSelected))
                {
                    _selectedProjectIds.Add(proj.Id);
                }

                // Apply filter and update UI
                ApplyProjectFilter();
                OnPropertyChanged(nameof(SelectedProjectsSummary));
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[R02] LoadProjectsAsync");
            ResultMessage = $"שגיאה לא צפויה: {ex.Message}";
            HasResult = true;
            ResultSuccess = false;
        }
        finally
        {
            _isLoadingProjects = false;
        }
    }

    private void OnProjectSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is SelectableProjectInfo project)
        {
            if (project.IsSelected)
            {
                _selectedProjectIds.Add(project.Id);
            }
            else
            {
                _selectedProjectIds.Remove(project.Id);
            }
            OnPropertyChanged(nameof(SelectedProjectsSummary));
        }
    }

    private void ApplyProjectFilter()
    {
        var searchLower = ProjectSearchText?.ToLower().Trim() ?? string.Empty;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            FilteredProjects.Clear();

            var filtered = string.IsNullOrEmpty(searchLower)
                ? _allProjects
                : _allProjects.Where(p =>
                    (p.ProjectNum?.ToLower().Contains(searchLower) ?? false) ||
                    (p.Name?.ToLower().Contains(searchLower) ?? false) ||
                    (p.CustomerName?.ToLower().Contains(searchLower) ?? false));

            foreach (var p in filtered)
            {
                FilteredProjects.Add(p);
            }
        });
    }

    private void ClearProjectSelection()
    {
        foreach (var project in _allProjects)
        {
            project.IsSelected = false;
        }
        _selectedProjectIds.Clear();
        ProjectSearchText = string.Empty;
        OnPropertyChanged(nameof(SelectedProjectsSummary));
    }

    private async Task LoadEmployeesAsync()
    {
        if (_reportService == null || _isLoadingEmployees) return;

        _isLoadingEmployees = true;
        try
        {
            var employees = await _reportService.GetEmployeesAsync(ActiveEmployeesOnly);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var previousSelections = new HashSet<int>(_selectedEmployeeIds);

                foreach (var emp in _allEmployees)
                {
                    emp.SelectionChanged -= OnEmployeeSelectionChanged;
                }

                _allEmployees.Clear();

                // Sort employees alphabetically (A-Z) by FullName
                var sortedEmployees = employees.OrderBy(e => e.FullName, StringComparer.CurrentCulture);

                foreach (var e in sortedEmployees)
                {
                    var selectable = new SelectableEmployeeInfo(e);

                    if (previousSelections.Contains(e.Id))
                    {
                        selectable.IsSelected = true;
                    }

                    selectable.SelectionChanged += OnEmployeeSelectionChanged;
                    _allEmployees.Add(selectable);
                }

                _selectedEmployeeIds.Clear();
                foreach (var emp in _allEmployees.Where(e => e.IsSelected))
                {
                    _selectedEmployeeIds.Add(emp.Id);
                }

                ApplyEmployeeFilter();
                OnPropertyChanged(nameof(SelectedEmployeesSummary));
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[R02] LoadEmployeesAsync");
            ResultMessage = $"שגיאה לא צפויה: {ex.Message}";
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
        if (sender is SelectableEmployeeInfo employee)
        {
            if (employee.IsSelected)
            {
                _selectedEmployeeIds.Add(employee.Id);
            }
            else
            {
                _selectedEmployeeIds.Remove(employee.Id);
            }
            OnPropertyChanged(nameof(SelectedEmployeesSummary));
        }
    }

    private void ApplyEmployeeFilter()
    {
        var searchLower = EmployeeSearchText?.ToLower().Trim() ?? string.Empty;

        var isOnUIThread = System.Windows.Application.Current.Dispatcher.CheckAccess();
        if (isOnUIThread)
        {
            ApplyEmployeeFilterCore(searchLower);
        }
        else
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyEmployeeFilterCore(searchLower));
        }
    }

    private void ApplyEmployeeFilterCore(string searchLower)
    {
        FilteredEmployees.Clear();

        var filtered = string.IsNullOrEmpty(searchLower)
            ? _allEmployees
            : _allEmployees.Where(e =>
                (e.FullName?.ToLower().Contains(searchLower) ?? false));

        foreach (var e in filtered)
        {
            FilteredEmployees.Add(e);
        }
    }

    private void ClearEmployeeSelection()
    {
        foreach (var employee in _allEmployees)
        {
            employee.IsSelected = false;
        }
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
                    ResultDetails = BuildResultDetails(result);
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

    private R02ReportRequest BuildRequest()
    {
        var request = new R02ReportRequest
        {
            StartDate = StartDate,
            EndDate = EndDate,
            ActiveProjectsOnly = ActiveProjectsOnly,
            ActiveEmployeesOnly = ActiveEmployeesOnly,
            ExcludeZeroHours = ExcludeZeroHours,
            IsClientExport = IsClientExport
        };

        // Multi-select customers
        if (_selectedCustomerIds.Count > 0)
        {
            foreach (var id in _selectedCustomerIds)
            {
                request.CustomerIds.Add(id);
            }

            var firstSelected = _allCustomers.FirstOrDefault(c => c.IsSelected);
            if (firstSelected != null)
            {
                request.CustomerId = _selectedCustomerIds.Count == 1 ? firstSelected.Id : null;
                request.CustomerName = _selectedCustomerIds.Count == 1 
                    ? firstSelected.Name 
                    : $"{_selectedCustomerIds.Count} לקוחות";
            }
        }

        // Multi-select projects: add all selected project IDs
        if (_selectedProjectIds.Count > 0)
        {
            foreach (var id in _selectedProjectIds)
            {
                request.ProjectIds.Add(id);
            }

            // For display purposes, set first selected project info
            var firstSelected = _allProjects.FirstOrDefault(p => p.IsSelected);
            if (firstSelected != null)
            {
                request.ProjectNum = _selectedProjectIds.Count == 1 
                    ? firstSelected.ProjectNum 
                    : $"{_selectedProjectIds.Count} פרויקטים";
                request.ProjectName = _selectedProjectIds.Count == 1 
                    ? firstSelected.Name 
                    : "מרובה";
            }
        }

        // Multi-select employees
        if (_selectedEmployeeIds.Count > 0)
        {
            foreach (var id in _selectedEmployeeIds)
            {
                request.EmployeeIds.Add(id);
            }
        }

        return request;
    }

    private static string BuildResultDetails(ReportGenerationResult result)
    {
        var details = new List<string>
        {
            $"דיווחים: {result.RowCount:N0}",
            $"מקור: {result.PrimarySource}"
        };

        if (result.Warnings.Count > 0)
        {
            details.Add("");
            details.Add("אזהרות:");
            details.AddRange(result.Warnings.Select(w => $"• {w}"));
        }

        return string.Join("\n", details);
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
