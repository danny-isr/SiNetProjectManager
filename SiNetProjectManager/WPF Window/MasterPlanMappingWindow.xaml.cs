using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.MVVM;
using SiNetSQL.Services;

namespace SiNetProjectManager.WPF_Window;

public partial class MasterPlanMappingWindow : Window
{
    public MasterPlanMappingWindow()
    {
        try
        {
            AppLogger.Info("[MasterPlanMapping] Window constructor started");
            InitializeComponent();
            AppLogger.Info("[MasterPlanMapping] InitializeComponent done, resolving ViewModel...");
            DataContext = App.ServiceProvider.GetRequiredService<MasterPlanMappingViewModel>();
            AppLogger.Info("[MasterPlanMapping] ViewModel resolved and set as DataContext");
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "[MasterPlanMapping] Window constructor FAILED");
            MessageBox.Show(
                $"שגיאה בפתיחת חלון מיפוי MasterPlan:\n{ex.Message}\n\nInner: {ex.InnerException?.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ActivateSelected_Click(object sender, RoutedEventArgs e) => SetSelectedActive(true);
    private void DeactivateSelected_Click(object sender, RoutedEventArgs e) => SetSelectedActive(false);

    private void SetSelectedActive(bool active)
    {
        int count = 0;

        if (MainTabControl.SelectedIndex == 0)
        {
            foreach (var item in CompaniesGrid.SelectedItems)
            {
                if (item is CompanyMappingRow row)
                {
                    row.IsActive = active;
                    count++;
                }
            }
        }
        else
        {
            foreach (var item in ContactsGrid.SelectedItems)
            {
                if (item is ContactMappingRow row)
                {
                    row.IsActive = active;
                    count++;
                }
            }
        }

        if (DataContext is MasterPlanMappingViewModel vm)
            vm.StatusMessage = $"{(active ? "✅ הופעלו" : "❌ בוטלו")} {count} רשומות";
    }
}
