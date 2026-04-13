using SiOffice.GoogleConnector.Reports.Data;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SiNetProjectManagerV2.ViewModels;

/// <summary>
/// Wrapper for ProjectInfo that adds selection state for multi-select scenarios.
/// </summary>
public class SelectableProjectInfo : INotifyPropertyChanged
{
    private bool _isSelected;

    public SelectableProjectInfo(ProjectInfo project)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
    }

    /// <summary>
    /// The underlying project info.
    /// </summary>
    public ProjectInfo Project { get; }

    /// <summary>
    /// Convenience properties for binding.
    /// NOTE: ProjectNum and Name use null-coalescing because Dapper can return null
    /// even for non-nullable record properties when database values are NULL.
    /// </summary>
    public int Id => Project.Id;
    public string ProjectNum => Project.ProjectNum ?? string.Empty;
    public string Name => Project.Name ?? string.Empty;
    public string? CustomerName => Project.CustomerName;
    public int? CustomerId => Project.CustomerId;

    /// <summary>
    /// Display text combining project number and name.
    /// </summary>
    public string DisplayText => $"{ProjectNum} - {Name}";

    /// <summary>
    /// Whether this project is selected for the report filter.
    /// </summary>
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

    /// <summary>
    /// Event raised when selection changes.
    /// </summary>
    public event EventHandler? SelectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
