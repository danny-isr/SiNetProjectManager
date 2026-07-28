using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public sealed class R03ReportViewModel : ObservableObject
{
    private readonly IMasterPlanR03ReportService _service;
    private readonly AsyncRelayCommand _generateCommand;
    private string _statusMessage = "בחר חודש ושנה ולחץ הפקה.";
    private bool _isBusy;
    private int _year = DateTime.Today.Year;
    private int _month = DateTime.Today.Month;
    private string? _resultUrl;

    public R03ReportViewModel(IMasterPlanR03ReportService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Years = new ObservableCollection<int>(Enumerable.Range(DateTime.Today.Year - 1, 3));
        Months = new ObservableCollection<int>(Enumerable.Range(1, 12));
        _generateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy);
    }

    public ObservableCollection<int> Years { get; }
    public ObservableCollection<int> Months { get; }
    public ICommand GenerateCommand => _generateCommand;

    public int Year
    {
        get => _year;
        set => SetField(ref _year, value);
    }

    public int Month
    {
        get => _month;
        set => SetField(ref _month, value);
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
                    new R03ReportRequest(Year, Month, Array.Empty<int>()),
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
            MessageBox.Show(ex.Message, "R03", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
