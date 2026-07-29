using System.ComponentModel;
using System.Runtime.CompilerServices;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

/// <summary>Checklist row for the R03 employee multi-select popup.</summary>
public sealed class SelectableR03Employee : INotifyPropertyChanged
{
    private bool _isSelected;

    public SelectableR03Employee(R03EmployeeInfo info)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
    }

    public R03EmployeeInfo Info { get; }

    public int Id => Info.EmployeeId;

    public string Name => Info.EmployeeName;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
