using System.ComponentModel;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.MVVM;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// User Management Dashboard window.
/// Allows administrators to view, search, edit, and toggle IsActive for all users.
/// </summary>
public partial class UserManagementWindow : Window
{
    public UserManagementWindow()
    {
        InitializeComponent();

        var vm = App.ServiceProvider.GetRequiredService<UserManagementViewModel>();
        DataContext = vm;

        _ = LoadMasterPlanEmployeesAsync(vm);
    }

    private async Task LoadMasterPlanEmployeesAsync(UserManagementViewModel vm)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[UserManagement][MasterPlanEmployees] Loading from ReplicaR03Repository...");
            
            string replicaCs = SiNetProjectManagerV2.Services.AppConfiguration.GetConnectionString("ReplicaDatabase") ?? "";
            if (string.IsNullOrEmpty(replicaCs))
            {
                string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (System.IO.File.Exists(configPath))
                {
                    string json = System.IO.File.ReadAllText(configPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connStrings) && 
                        connStrings.TryGetProperty("ReplicaDatabase", out var repDb))
                    {
                        replicaCs = repDb.GetString() ?? "";
                    }
                }
            }

            var repo = new SiOffice.GoogleConnector.Reports.Data.ReplicaR03Repository(replicaCs);
            var employees = await repo.GetEmployeesAsync(activeOnly: false);
            
            System.Diagnostics.Debug.WriteLine($"[UserManagement][MasterPlanEmployees] Loaded count={employees.Count}");
            if (employees.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[UserManagement][MasterPlanEmployees] First item: Id={employees[0].Id}, Display={employees[0].Name}");
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Add a null/empty option for "No Mapping"
                vm.MasterPlanEmployees.Add(new UserManagementViewModel.MasterPlanEmployeeDto(null, "-- ללא קישור --"));

                foreach (var emp in employees.OrderBy(e => e.Name))
                {
                    vm.MasterPlanEmployees.Add(new UserManagementViewModel.MasterPlanEmployeeDto(emp.Id, emp.Name));
                }
                
                System.Diagnostics.Debug.WriteLine($"[UserManagement][MasterPlanEmployees] Assigned to ViewModel count={vm.MasterPlanEmployees.Count}");
                vm.UpdateMasterPlanEmployeeNames();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load MasterPlan employees for User Management: {ex.Message}");
        }
    }

    /// <summary>
    /// Forces the ComboBox SelectedItem binding to push the selected value
    /// back to the source property. Workaround for a known WPF issue where
    /// ComboBox.SelectedItem inside a read-only DataGrid CellTemplate may
    /// silently fail to propagate selection changes through TwoWay binding.
    /// </summary>
    private void OnEditComboBoxSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ComboBox cb)
        {
            var binding = cb.GetBindingExpression(
                System.Windows.Controls.Primitives.Selector.SelectedItemProperty);
            binding?.UpdateSource();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is UserManagementViewModel vm && vm.HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "יש שינויים שלא נשמרו. האם לשמור אותם לפני סגירת החלון?",
                "שינויים לא נשמרו",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    vm.SaveCommand.Execute(null);
                    break;
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    break;
            }
        }
    }
}
