using SiNet.Application.Billing;
using SiNet.App.Wpf.Inspection;

namespace SiNet.App.Wpf.Billing;

public sealed class BillingHourlySubContractChoiceVm : ObservableObject
{
    private bool _isSelected;
    private int _reportCount;
    private decimal _totalHours;

    public BillingHourlySubContractChoiceVm(BillingHourlySubContractDraft source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public BillingHourlySubContractDraft Source { get; }
    public int MasterPlanSubContractId => Source.MasterPlanSubContractId;
    public string Name => Source.Name;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
                SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int ReportCount
    {
        get => _reportCount;
        set => SetField(ref _reportCount, value);
    }

    public decimal TotalHours
    {
        get => _totalHours;
        set => SetField(ref _totalHours, value);
    }

    public string AutomationId =>
        "BillingDashboard.HourlySubContract." + MasterPlanSubContractId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public event EventHandler? SelectionChanged;
}
