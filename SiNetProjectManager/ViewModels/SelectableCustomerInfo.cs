using SiOffice.GoogleConnector.Reports.Data;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SiNetProjectManager.ViewModels;

/// <summary>
/// Wrapper for CustomerInfo that adds selection state for multi-select scenarios.
/// </summary>
public class SelectableCustomerInfo : INotifyPropertyChanged
{
    private bool _isSelected;

    public SelectableCustomerInfo(CustomerInfo customer)
    {
        Customer = customer ?? throw new ArgumentNullException(nameof(customer));
    }

    /// <summary>
    /// The underlying customer info.
    /// </summary>
    public CustomerInfo Customer { get; }

    /// <summary>
    /// Convenience properties for binding.
    /// </summary>
    public int Id => Customer.Id;
    public string Name => Customer.Name ?? string.Empty;

    /// <summary>
    /// Whether this customer is selected for the report filter.
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
