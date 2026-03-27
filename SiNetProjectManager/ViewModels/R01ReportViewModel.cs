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

namespace SiNetProjectManager.ViewModels;

/// <summary>
/// ViewModel for the R01 Project Portfolio Dashboard report dialog.
/// Supports multi-select projects with search functionality.
/// </summary>
public class R01ReportViewModel : INotifyPropertyChanged
{
#pragma warning disable CS0649 // Field is assigned via reflection in Initialize()
    private R01ReportService? _reportService;
#pragma warning restore CS0649
    private CancellationTokenSource? _cts;

    // Customer multi-select collections
    private readonly List<SelectableCustomerInfo> _allCustomers = new();
    private readonly HashSet<int> _selectedCustomerIds = new();

    // Project multi-select collections
    private readonly List<SelectableProjectInfo> _allProjects = new();
    private readonly HashSet<int> _selectedProjectIds = new();

    // Guard against concurrent calls
    private bool _isLoadingProjects;
    private bool _isLoadingCustomers;

    #region Constructor

    public R01ReportViewModel()
    {
        // Initialize commands
        GenerateCommand = new RelayCommand<object?>(_ => GenerateReportAsync(), _ => CanGenerate());
        CancelCommand = new RelayCommand<object?>(_ => Cancel(), _ => IsGenerating);
        OpenUrlCommand = new RelayCommand<object?>(_ => OpenUrl(), _ => !string.IsNullOrEmpty(ResultUrl));
        ClearProjectSelectionCommand = new RelayCommand<object?>(_ => ClearProjectSelection());
        ClearCustomerSelectionCommand = new RelayCommand<object?>(_ => ClearCustomerSelection());

        // Initialize collections
        FilteredCustomers = new ObservableCollection<SelectableCustomerInfo>();
        FilteredProjects = new ObservableCollection<SelectableProjectInfo>();
    }

    /// <summary>
    /// Initializes the ViewModel with the report service.
    /// Call this after construction to enable report generation.
    /// </summary>
    public void Initialize(R01ReportService reportService)
    {
        AppLogger.Debug($"[R01] Initialize: START");
        if (_reportService != null)
        {
            AppLogger.Debug($"[R01] Initialize: Already initialized, skipping");
            return; // Already initialized
        }

        _reportService = reportService;

        // Load HourPrice default from Management Settings
        var managementSettings = ManagementSettingsManager.LoadSettings();
        HourPrice = managementSettings.HourPriceDefault;
        AppLogger.Debug($"[R01] Initialize: HourPrice set to {HourPrice}");

        // Load filter data
        AppLogger.Debug($"[R01] Initialize: Starting LoadFilterDataAsync (fire-and-forget)");
        _ = LoadFilterDataAsync();
    }

    #endregion

    #region Bindable Properties

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

    public ObservableCollection<SelectableProjectInfo> FilteredProjects { get; }

    private string _projectSearchText = string.Empty;
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
    public bool IsProjectPopupOpen
    {
        get => _isProjectPopupOpen;
        set { _isProjectPopupOpen = value; OnPropertyChanged(); }
    }

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

    public ICommand ClearProjectSelectionCommand { get; }

    private bool _activeProjectsOnly = true;
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

    private decimal _hourPrice = 280m;
    public decimal HourPrice
    {
        get => _hourPrice;
        set
        {
            if (value == _hourPrice) return;
            _hourPrice = value;
            OnPropertyChanged();
        }
    }

    // --- Progress Properties ---

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

    // --- Result Properties ---

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

        AppLogger.Debug($"[R01] LoadFilterDataAsync: START");
        try
        {
            await LoadCustomersAsync();
            await LoadProjectsAsync();
            AppLogger.Debug($"[R01] LoadFilterDataAsync: END");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[R01] LoadFilterDataAsync EXCEPTION");
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
            AppLogger.Debug($"[R01] LoadCustomersAsync: Retrieved {customers.Count} customers");

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Unsubscribe old event handlers
                foreach (var cust in _allCustomers)
                {
                    cust.SelectionChanged -= OnCustomerSelectionChanged;
                }

                _allCustomers.Clear();

                // Sort customers alphabetically by name (A-Z)
                var sortedCustomers = customers.OrderBy(c => c.Name ?? string.Empty, StringComparer.CurrentCulture);

                foreach (var c in sortedCustomers)
                {
                    var selectable = new SelectableCustomerInfo(c);
                    selectable.SelectionChanged += OnCustomerSelectionChanged;
                    _allCustomers.Add(selectable);
                }

                ApplyCustomerFilter();
                OnPropertyChanged(nameof(SelectedCustomersSummary));
            });
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
            AppLogger.Debug($"[R01] OnCustomerSelectionChanged: CustomerId={customer.Id}, Name={customer.Name}, IsSelected={customer.IsSelected}");

            if (customer.IsSelected)
            {
                _selectedCustomerIds.Add(customer.Id);
            }
            else
            {
                _selectedCustomerIds.Remove(customer.Id);
            }

            AppLogger.Debug($"[R01] OnCustomerSelectionChanged: _selectedCustomerIds.Count={_selectedCustomerIds.Count}");
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
            : _allCustomers.Where(c => c.Name?.ToLower().Contains(searchLower) ?? false);

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
        AppLogger.Debug($"[R01] LoadProjectsAsync: START");
        try
        {
            // Get projects filtered by selected customers (null = all customers)
            int? customerId = _selectedCustomerIds.Count == 1 
                ? _selectedCustomerIds.First() 
                : null;

            var projects = await _reportService.GetProjectsAsync(customerId, ActiveProjectsOnly);
            AppLogger.Debug($"[R01] LoadProjectsAsync: Retrieved {projects.Count} projects");

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var previousSelections = new HashSet<int>(_selectedProjectIds);

                foreach (var proj in _allProjects)
                {
                    proj.SelectionChanged -= OnProjectSelectionChanged;
                }

                _allProjects.Clear();

                // Sort projects by ProjectNum numerically (descending - highest first)
                var sortedProjects = projects.OrderByDescending(p => 
                    int.TryParse(p.ProjectNum, out var num) ? num : int.MinValue);

                foreach (var p in sortedProjects)
                {
                    var selectable = new SelectableProjectInfo(p);

                    if (previousSelections.Contains(p.Id))
                    {
                        selectable.IsSelected = true;
                    }

                    selectable.SelectionChanged += OnProjectSelectionChanged;
                    _allProjects.Add(selectable);
                }

                _selectedProjectIds.Clear();
                foreach (var proj in _allProjects.Where(p => p.IsSelected))
                {
                    _selectedProjectIds.Add(proj.Id);
                }

                ApplyProjectFilter();
                OnPropertyChanged(nameof(SelectedProjectsSummary));
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[R01] LoadProjectsAsync EXCEPTION");
        }
        finally
        {
            _isLoadingProjects = false;
            AppLogger.Debug($"[R01] LoadProjectsAsync: END");
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

        var isOnUIThread = System.Windows.Application.Current.Dispatcher.CheckAccess();
        if (isOnUIThread)
        {
            ApplyProjectFilterCore(searchLower);
        }
        else
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyProjectFilterCore(searchLower));
        }
    }

    private void ApplyProjectFilterCore(string searchLower)
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

            // Update UI with results
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

    private R01ReportRequest BuildRequest()
    {
        // DIAGNOSTIC: Log current selection state
        AppLogger.Debug($"[R01] BuildRequest: _selectedCustomerIds.Count={_selectedCustomerIds.Count}");
        AppLogger.Debug($"[R01] BuildRequest: _selectedProjectIds.Count={_selectedProjectIds.Count}");

        if (_selectedCustomerIds.Count > 0)
        {
            AppLogger.Debug($"[R01] BuildRequest: Selected CustomerIds = [{string.Join(", ", _selectedCustomerIds)}]");
        }

        var request = new R01ReportRequest
        {
            ActiveOnly = ActiveProjectsOnly,
            HourPrice = HourPrice
        };

        // Multi-select customers: add all selected customer IDs
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

        // DIAGNOSTIC: Log final request state
        AppLogger.Debug($"[R01] BuildRequest: request.CustomerIds.Count={request.CustomerIds.Count}, request.CustomerId={request.CustomerId}");

        return request;
    }

    private static string BuildResultDetails(ReportGenerationResult result)
    {
        var details = new List<string>
        {
            $"פרויקטים: {result.RowCount:N0}",
            $"מקור: {result.PrimarySource}",
            $"מצב: {(result.ReportMode == "Full" ? "מלא (כולל KPI)" : "בסיסי")}"
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
        catch
        {
            // Ignore - URL might not be valid
        }
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
