using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Configuration;

namespace SiNet.App.Wpf.Admin.Security;

public sealed class SecretSetupViewModel : ObservableObject
{
    private readonly ISecretSetupService _secretSetupService;
    private string _summaryMessage = string.Empty;
    private bool _isBusy;
    private string? _pendingGoogleJsonContent;

    public SecretSetupViewModel(ISecretSetupService secretSetupService)
    {
        _secretSetupService = secretSetupService ?? throw new ArgumentNullException(nameof(secretSetupService));
        Rows = new ObservableCollection<SecretRowViewModel>(
            SecretCatalog.All.Select(e => new SecretRowViewModel(e)));
        SaveCommand = new AsyncRelayCommand(SaveAndValidateAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        BrowseGoogleCredentialsCommand = new RelayCommand(_ => BrowseGoogleCredentials());
    }

    public ObservableCollection<SecretRowViewModel> Rows { get; }

    public string SummaryMessage
    {
        get => _summaryMessage;
        private set => SetField(ref _summaryMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public ICommand BrowseGoogleCredentialsCommand { get; }

    public event Action<bool>? RequestClose;

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var snapshot = await _secretSetupService.GetEditableSnapshotAsync().ConfigureAwait(true);
            var statuses = await _secretSetupService.GetStatusesAsync().ConfigureAwait(true);

            foreach (var row in Rows)
            {
                if (snapshot.PrefillValues.TryGetValue(row.Key, out var value))
                {
                    row.TextValue = value ?? string.Empty;
                }

                if (row.IsJsonFile)
                {
                    row.JsonFileLabel = snapshot.GoogleConfiguredDisplay;
                }

                var status = statuses.First(s => s.Key == row.Key);
                row.ApplyStatus(status);
            }

            SummaryMessage = "טען סטטוס מפתחות מה-Vault.";
        }
        catch (Exception ex)
        {
            SummaryMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BrowseGoogleCredentials()
    {
        var dialog = new OpenFileDialog
        {
            Title = "בחר קובץ credentials.json",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _pendingGoogleJsonContent = File.ReadAllText(dialog.FileName);
            var googleRow = Rows.First(r => r.Key == SecretCatalog.GoogleClientSecrets);
            googleRow.JsonFileLabel = dialog.FileName;
        }
        catch (Exception ex)
        {
            SummaryMessage = $"שגיאה בקריאת הקובץ: {ex.Message}";
        }
    }

    private async Task SaveAndValidateAsync()
    {
        IsBusy = true;
        SummaryMessage = "שומר ובודק...";

        try
        {
            var updates = new Dictionary<string, string?>();
            foreach (var row in Rows)
            {
                var pending = row.GetPendingValue();
                if (pending is not null)
                {
                    updates[row.Key] = pending;
                }
            }

            if (!string.IsNullOrWhiteSpace(_pendingGoogleJsonContent))
            {
                updates[SecretCatalog.GoogleClientSecrets] = _pendingGoogleJsonContent;
            }

            var result = await _secretSetupService
                .SaveAndValidateAsync(new SecretSetupUpdateDto(updates))
                .ConfigureAwait(true);

            await ApplyValidationResultsAsync(result).ConfigureAwait(true);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"נשמרו {result.SavedCount} מפתחות ב-Credential Manager.");
            foreach (var line in result.PassedSummaries)
            {
                sb.AppendLine($"✅ {line}");
            }

            foreach (var line in result.FailedSummaries)
            {
                sb.AppendLine($"❌ {line}");
            }

            SummaryMessage = sb.ToString().Trim();

            if (result.AllPassed)
            {
                RequestClose?.Invoke(true);
            }
        }
        catch (Exception ex)
        {
            SummaryMessage = $"שגיאה בשמירה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyValidationResultsAsync(SecretSaveResultDto result)
    {
        var statuses = await _secretSetupService.GetStatusesAsync().ConfigureAwait(true);
        foreach (var row in Rows)
        {
            row.ApplyStatus(statuses.First(s => s.Key == row.Key));
        }
    }
}
