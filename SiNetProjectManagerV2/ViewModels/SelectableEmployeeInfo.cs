using SiOffice.GoogleConnector.Reports.Data;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SiNetProjectManagerV2.ViewModels;

/// <summary>
/// Wrapper for EmployeeInfo that adds selection state for multi-select scenarios.
/// </summary>
public class SelectableEmployeeInfo : INotifyPropertyChanged
{
    private bool _isSelected;

    public SelectableEmployeeInfo(EmployeeInfo employee)
    {
        Employee = employee ?? throw new ArgumentNullException(nameof(employee));
    }

    /// <summary>
    /// The underlying employee info.
    /// </summary>
    public EmployeeInfo Employee { get; }

    /// <summary>
    /// Convenience properties for binding.
    /// </summary>
    public int Id => Employee.Id;
    public string FirstName => Employee.FirstName ?? string.Empty;
    public string LastName => Employee.LastName ?? string.Empty;
    public string FullName => Employee.FullName;

    /// <summary>
    /// Whether this employee is selected for the report filter.
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

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
