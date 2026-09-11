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
        set
        {
            if (SetField(ref _totalHours, value))
            {
                OnPropertyChanged(nameof(PricedAmount));
                OnPropertyChanged(nameof(AmountText));
                OnPropertyChanged(nameof(RateBasisText));
            }
        }
    }

    public BillingPricedValue PricedAmount =>
        BillingPreparationAmountCalculator.Hourly(Source, TotalHours);

    public string AmountText =>
        PricedAmount.IsPriced
            ? BillingMoneyFormatter.FormatShekels(PricedAmount.Amount!.Value)
            : (PricedAmount.UnavailableReason ?? "תעריף לא ניתן לקביעה");

    public string RateBasisText =>
        Source.UniqueHourlyRate is decimal rate
            ? "תעריף: " + BillingMoneyFormatter.FormatShekels(rate)
            : (Source.AmountUnavailableReason ?? "תעריף לא ניתן לקביעה");

    public string AutomationId =>
        "BillingDashboard.HourlySubContract." + MasterPlanSubContractId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public event EventHandler? SelectionChanged;
}
