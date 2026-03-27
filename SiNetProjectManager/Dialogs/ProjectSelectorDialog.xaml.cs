using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetProjectManager.Dialogs;

/// <summary>
/// Dialog for selecting a project.
/// Uses SearchableProjectSelector for high-performance filtering.
/// </summary>
public partial class ProjectSelectorDialog : Window, INotifyPropertyChanged
{
    private ObservableCollection<Project> _projects = new();
    private Project? _selectedProject;

    public ObservableCollection<Project> Projects
    {
        get => _projects;
        set { _projects = value; OnPropertyChanged(); }
    }

    public Project? SelectedProject
    {
        get => _selectedProject;
        set { _selectedProject = value; OnPropertyChanged(); }
    }

    public ProjectSelectorDialog()
    {
        InitializeComponent();
        DataContext = this;
        LoadProjects();
    }

    private void LoadProjects()
    {
        try
        {
            // Use DbContextFactory from DI container for proper disposal
            var dbContextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
            using var context = dbContextFactory.CreateDbContext();

            // Load projects with navigation properties for SearchableProjectSelector filtering
            var projects = context.Projects
                .Include(p => p.Place)
                .Include(p => p.Company)
                .Where(p => p.NameAndNumber != null)
                .OrderByDescending(p => p.Number)
                .AsNoTracking()
                .ToList();

            Projects = new ObservableCollection<Project>(projects);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בטעינת פרויקטים: {ex.Message}", "שגיאה", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject != null)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("נא לבחור פרויקט", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
