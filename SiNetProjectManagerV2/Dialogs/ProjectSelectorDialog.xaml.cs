using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.DTOs.Email;
using SiNetSQL.Models;

namespace SiNetProjectManagerV2.Dialogs;

/// <summary>
/// Dialog for selecting a project.
/// Uses SearchableProjectSelector for high-performance filtering.
/// </summary>
public partial class ProjectSelectorDialog : Window, INotifyPropertyChanged
{
    private readonly List<Project> _allLoadedProjects = new();
    private ObservableCollection<Project> _projects = new();
    private Project? _selectedProject;
    private bool _showOnlyWithChildren;

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

    public bool ShowOnlyWithChildren
    {
        get => _showOnlyWithChildren;
        set
        {
            if (_showOnlyWithChildren != value)
            {
                _showOnlyWithChildren = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    public ProjectSelectorDialog(SuggestedActionType? actionType = null)
    {
        InitializeComponent();
        DataContext = this;
        // Default to true for review-creation or parent project setup actions
        _showOnlyWithChildren = actionType == SuggestedActionType.CreateNewReview;
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
                .Include(p => p.InverseOnerProject)
                .Where(p => p.NameAndNumber != null)
                .OrderByDescending(p => p.Number)
                .AsNoTracking()
                .ToList();

            _allLoadedProjects.Clear();
            _allLoadedProjects.AddRange(projects);

            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בטעינת פרויקטים: {ex.Message}", "שגיאה", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        var filtered = _showOnlyWithChildren
            ? _allLoadedProjects.Where(p => p.InverseOnerProject.Any()).ToList()
            : _allLoadedProjects;

        Projects = new ObservableCollection<Project>(filtered);
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
