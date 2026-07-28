using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public sealed class R02ReportViewModel : ObservableObject
{
    private readonly IMasterPlanR02ReportService _service;
    private readonly AsyncRelayCommand _generateCommand;
    private string _statusMessage = "הפקת שעות עבודה (R02).";
    private bool _isBusy;
    private DateTime _startDate = DateTime.Today.AddMonths(-1);
    private DateTime _endDate = DateTime.Today;
    private bool _excludeZeroHours = true;
    private string? _resultUrl;

    public R02ReportViewModel(IMasterPlanR02ReportService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _generateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy);
    }

    public ICommand GenerateCommand => _generateCommand;

    public DateTime StartDate
    {
        get => _startDate;
        set => SetField(ref _startDate, value);
    }

    public DateTime EndDate
    {
        get => _endDate;
        set => SetField(ref _endDate, value);
    }

    public bool ExcludeZeroHours
    {
        get => _excludeZeroHours;
        set => SetField(ref _excludeZeroHours, value);
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
                    new R02ReportRequest(StartDate, EndDate, ExcludeZeroHours: ExcludeZeroHours),
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
            MessageBox.Show(ex.Message, "R02", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
