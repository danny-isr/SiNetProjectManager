using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Task Import window for pasting TSV data, previewing, and committing to DB.
/// Optionally receives project context from the floating tasks window.
/// </summary>
public partial class TaskImportWindow : Window
{
    private readonly TaskImportViewModel _viewModel;
    private DataGrid? _previewDataGrid;

    public TaskImportWindow(int? activeProjectId = null, string? activeProjectDisplay = null)
    {
        InitializeComponent();

        // Resolve ViewModel from DI container with optional project context
        var dbContextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        _viewModel = new TaskImportViewModel(dbContextFactory, activeProjectId, activeProjectDisplay);
        DataContext = _viewModel;

        // 🔧 NUCLEAR OPTION: Hook into Window's Loaded event to find DataGrid and attach handler
        Loaded += OnWindowLoaded;
        
        // Subscribe to ViewModel's mapping applied event
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Find the DataGrid by name after window is fully loaded
        _previewDataGrid = FindName("PreviewDataGrid") as DataGrid;
        
        if (_previewDataGrid != null)
        {
            _previewDataGrid.LoadingRow += OnDataGridLoadingRow;
            System.Diagnostics.Debug.WriteLine("[TaskImport] Successfully attached LoadingRow handler to DataGrid");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[TaskImport] WARNING: Could not find PreviewDataGrid!");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When mapping is applied, force DataGrid to refresh rows
        if (e.PropertyName == nameof(TaskImportViewModel.IsMappingApplied) && _viewModel.IsMappingApplied)
        {
            System.Diagnostics.Debug.WriteLine("[TaskImport] IsMappingApplied changed to true - forcing DataGrid refresh");
            ForceDataGridRefresh();
        }
    }

    /// <summary>
    /// Forces the DataGrid to re-render all rows by manually applying colors to existing row objects.
    /// This is more aggressive than ItemsSource refresh and works even with row virtualization.
    /// </summary>
    private void ForceDataGridRefresh()
    {
        if (_previewDataGrid == null)
            return;

        System.Diagnostics.Debug.WriteLine("[TaskImport] Forcing DataGrid refresh - applying colors to existing rows");

        // Method 1: Apply colors to all currently visible rows directly
        int coloredRows = 0;
        
        for (int i = 0; i < _previewDataGrid.Items.Count; i++)
        {
            // Get the row container for this item
            var row = _previewDataGrid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow;
            
            if (row != null && row.Item is TaskImportPreviewRow previewRow)
            {
                // Apply colors directly to the existing row object
                ApplyColorToRow(row, previewRow);
                coloredRows++;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[TaskImport] Applied colors to {coloredRows} existing rows");

        // Method 2: Also force a visual refresh to catch any virtualized rows
        _previewDataGrid.Items.Refresh();
        _previewDataGrid.UpdateLayout();
        
        // Method 3: If rows are virtualized, scroll to trigger LoadingRow for off-screen rows
        if (_previewDataGrid.Items.Count > 0)
        {
            _previewDataGrid.ScrollIntoView(_previewDataGrid.Items[0]);
            _previewDataGrid.UpdateLayout();
        }

        System.Diagnostics.Debug.WriteLine("[TaskImport] DataGrid refresh complete");
    }

    /// <summary>
    /// Applies color styling to a specific DataGridRow based on its mapping.
    /// </summary>
    private void ApplyColorToRow(DataGridRow row, TaskImportPreviewRow previewRow)
    {
        if (previewRow.IsTask)
        {
            row.Background = new SolidColorBrush(Color.FromRgb(227, 242, 253)); // #E3F2FD (Light Blue)
            row.BorderBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243)); // #2196F3 (Blue border)
            row.BorderThickness = new Thickness(0, 0, 0, 2);
            
            System.Diagnostics.Debug.WriteLine($"[TaskImport] Row {previewRow.RowNumber}: Applied BLUE (Task)");
        }
        else if (previewRow.IsDecision)
        {
            row.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233)); // #E8F5E9 (Light Green)
            row.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // #4CAF50 (Green border)
            row.BorderThickness = new Thickness(0, 0, 0, 2);
            
            System.Diagnostics.Debug.WriteLine($"[TaskImport] Row {previewRow.RowNumber}: Applied GREEN (Decision)");
        }
        else
        {
            // No mapping applied yet - default white
            row.Background = Brushes.White;
            row.BorderBrush = Brushes.Transparent;
            row.BorderThickness = new Thickness(0);
        }
    }

    /// <summary>
    /// Forces row background colors programmatically when XAML styling fails.
    /// This is a fallback for when DataTriggers are being overridden by theme/global styles.
    /// </summary>
    private void OnDataGridLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not TaskImportPreviewRow row)
            return;

        // Use the shared color application method
        ApplyColorToRow(e.Row, row);
    }
}
