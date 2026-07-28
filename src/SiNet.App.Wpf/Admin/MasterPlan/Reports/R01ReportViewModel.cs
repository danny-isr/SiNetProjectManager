using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public sealed class R01ReportViewModel : ObservableObject
{
    private readonly IMasterPlanR01ReportService _service;
    private readonly AsyncRelayCommand _generateCommand;
    private string _statusMessage = "הפקת סיכום שעות (R01).";
    private bool _isBusy;
    private bool _activeOnly = true;
    private decimal _hourPrice = 280m;
    private string? _resultUrl;

    public R01ReportViewModel(IMasterPlanR01ReportService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _generateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy);
    }

    public ICommand GenerateCommand => _generateCommand;

    public bool ActiveOnly
    {
        get => _activeOnly;
        set => SetField(ref _activeOnly, value);
    }

    public decimal HourPrice
    {
        get => _hourPrice;
        set => SetField(ref _hourPrice, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                _generateCommand.RaiseCanExecuteChanged();
        }
    }

    public string? ResultUrl
    {
        get => _resultUrl;
        private set => SetField(ref _resultUrl, value);
    }

    private async Task GenerateAsync()
    {
        IsBusy = true;
        ResultUrl = null;
        try
        {
            var progress = new Progress<(string Phase, string Message, int Percent)>(p =>
                StatusMessage = $"{p.Percent}% — {p.Message}");

            var result = await _service.GenerateAsync(
                    new R01ReportRequest(ActiveOnly: ActiveOnly, HourPrice: HourPrice),
                    progress)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                StatusMessage = result.Error ?? "הפקה נכשלה.";
                return;
            }

            ResultUrl = result.Url;
            StatusMessage = $"הושלם ({result.RowCount} שורות).";
            if (!string.IsNullOrWhiteSpace(result.Url))
                Process.Start(new ProcessStartInfo(result.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            MessageBox.Show(ex.Message, "R01", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
