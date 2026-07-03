using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.App.Wpf.Admin.Security;

public sealed class SecretSetupViewModel : ObservableObject
{
    private readonly ISecretSetupService _secretSetupService;
    private string _summaryMessage = string.Empty;
    private bool _isBusy;
    private string? _pendingGoogleJsonContent;
    private string _accServiceModeSummary = "מצב ACC: טוען...";
    private string _accServiceKeySummary = "מפתח ACC: טוען...";
    private string _accServiceHealthSummary = "בריאות שירות ACC: טוען...";
    private string _accServiceDiagnosticsSummary = "אבחון ACC: טוען...";

    private readonly IAccServiceModeProvider _accServiceModeProvider;
    private readonly IAccServiceKeyDiagnostics _accServiceKeyDiagnostics;
    private readonly IAccServiceHealthProbe _accServiceHealthProbe;
    private readonly IAccServiceDiagnosticsProbe _accServiceDiagnosticsProbe;

    public SecretSetupViewModel(
        ISecretSetupService secretSetupService,
        IAccServiceModeProvider accServiceModeProvider,
        IAccServiceKeyDiagnostics accServiceKeyDiagnostics,
        IAccServiceHealthProbe accServiceHealthProbe,
        IAccServiceDiagnosticsProbe accServiceDiagnosticsProbe)
    {
        _secretSetupService = secretSetupService ?? throw new ArgumentNullException(nameof(secretSetupService));
        _accServiceModeProvider = accServiceModeProvider ?? throw new ArgumentNullException(nameof(accServiceModeProvider));
        _accServiceKeyDiagnostics = accServiceKeyDiagnostics ?? throw new ArgumentNullException(nameof(accServiceKeyDiagnostics));
        _accServiceHealthProbe = accServiceHealthProbe ?? throw new ArgumentNullException(nameof(accServiceHealthProbe));
        _accServiceDiagnosticsProbe = accServiceDiagnosticsProbe ?? throw new ArgumentNullException(nameof(accServiceDiagnosticsProbe));
        Rows = new ObservableCollection<SecretRowViewModel>(
            SecretCatalog.All.Select(e => new SecretRowViewModel(e)));
        SaveCommand = new AsyncRelayCommand(SaveAndValidateAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsBusy);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy);
        BrowseGoogleCredentialsCommand = new RelayCommand(_ => BrowseGoogleCredentials());
        GenerateAccServiceKeyCommand = new AsyncRelayCommand(GenerateAccServiceKeyAsync, () => !IsBusy);
        TestAccServiceCommand = new AsyncRelayCommand(TestAccServiceAsync, () => !IsBusy);
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
                ExportCommand.RaiseCanExecuteChanged();
                ImportCommand.RaiseCanExecuteChanged();
                GenerateAccServiceKeyCommand.RaiseCanExecuteChanged();
                TestAccServiceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    public AsyncRelayCommand GenerateAccServiceKeyCommand { get; }

    public AsyncRelayCommand TestAccServiceCommand { get; }

    public ICommand BrowseGoogleCredentialsCommand { get; }

    public string AccServiceModeSummary
    {
        get => _accServiceModeSummary;
        private set => SetField(ref _accServiceModeSummary, value);
    }

    public string AccServiceKeySummary
    {
        get => _accServiceKeySummary;
        private set => SetField(ref _accServiceKeySummary, value);
    }

    public string AccServiceHealthSummary
    {
        get => _accServiceHealthSummary;
        private set => SetField(ref _accServiceHealthSummary, value);
    }

    public string AccServiceDiagnosticsSummary
    {
        get => _accServiceDiagnosticsSummary;
        private set => SetField(ref _accServiceDiagnosticsSummary, value);
    }

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

                row.ApplyStatus(statuses.First(s => s.Key == row.Key));
            }

            await RefreshAccControlPlaneAsync().ConfigureAwait(true);
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

    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "ייצוא חבילת סודות",
            Filter = "SiNet Secrets (*.secrets)|*.secrets",
            DefaultExt = ".secrets",
            FileName = "SiNet.secrets",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var passwordDialog = new ProvisioningPasswordWindow(
            requireConfirmation: true,
            title: "ייצוא חבילת הגדרות מוצפנת");
        if (passwordDialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _secretSetupService
                .ExportAsync(dialog.FileName, passwordDialog.EnteredPassword)
                .ConfigureAwait(true);

            SummaryMessage = result.Message;
            MessageBox.Show(result.Message, "ייצוא הצליח", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SummaryMessage = ex.Message;
            MessageBox.Show(ex.Message, "שגיאה בייצוא", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "ייבוא חבילת סודות",
            Filter = "SiNet Secrets (*.secrets)|*.secrets|All Files (*.*)|*.*",
            DefaultExt = ".secrets",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var passwordDialog = new ProvisioningPasswordWindow(
            requireConfirmation: false,
            title: "ייבוא חבילת הגדרות");
        if (passwordDialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var preview = await _secretSetupService
                .PreviewImportAsync(dialog.FileName, passwordDialog.EnteredPassword)
                .ConfigureAwait(true);

            var previewText = BuildImportPreviewMessage(preview);
            var existingCount = preview.Items.Count(i => i.ExistsInVault);
            var overwrite = existingCount == 0;

            if (existingCount > 0)
            {
                var answer = MessageBox.Show(
                    previewText + Environment.NewLine + Environment.NewLine +
                    "חלק מהמפתחות כבר קיימים ב-Vault. לדרוס?",
                    "אישור ייבוא",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                overwrite = answer == MessageBoxResult.Yes;
            }
            else
            {
                var answer = MessageBox.Show(
                    previewText,
                    "אישור ייבוא",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Information);
                if (answer != MessageBoxResult.OK)
                {
                    return;
                }
            }

            var result = await _secretSetupService
                .ImportAsync(dialog.FileName, passwordDialog.EnteredPassword, overwrite)
                .ConfigureAwait(true);

            await RefreshAfterImportAsync().ConfigureAwait(true);

            var sb = new StringBuilder(result.Message);
            foreach (var line in result.SkippedSummaries)
            {
                sb.AppendLine(line);
            }

            SummaryMessage = sb.ToString().Trim();
            MessageBox.Show(SummaryMessage, "ייבוא הושלם", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SummaryMessage = ex.Message;
            MessageBox.Show(ex.Message, "שגיאה בייבוא", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildImportPreviewMessage(SecretImportPreviewDto preview)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"מפתחות לייבוא: {preview.KeysToImportCount}");
        foreach (var item in preview.Items)
        {
            var status = item.ExistsInVault ? "(קיים ב-Vault)" : "(חדש)";
            sb.AppendLine($"• {item.DisplayName} {status}");
        }

        if (preview.UnknownKeyCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"מפתחות לא מוכרים שידולגו: {preview.UnknownKeyCount}");
        }

        return sb.ToString().Trim();
    }

    private async Task RefreshAfterImportAsync()
    {
        await LoadAsync().ConfigureAwait(true);
        var validation = await _secretSetupService
            .SaveAndValidateAsync(new SecretSetupUpdateDto(new Dictionary<string, string?>()))
            .ConfigureAwait(true);
        await ApplyValidationResultsAsync(validation).ConfigureAwait(true);
    }

    private async Task GenerateAccServiceKeyAsync()
    {
        IsBusy = true;
        try
        {
            var key = await _secretSetupService.GenerateAccServiceApiKeyAsync().ConfigureAwait(true);
            var row = Rows.First(r => r.Key == SecretCatalog.AccServiceApiKey);
            row.TextValue = key;
            await LoadAsync().ConfigureAwait(true);
            SummaryMessage = "נוצר AccService API Key חדש ונשמר ב-Vault.";
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

    private async Task TestAccServiceAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _secretSetupService.TestAccServiceAsync().ConfigureAwait(true);
            var row = Rows.First(r => r.Key == SecretCatalog.AccServiceApiKey);
            row.ApplyStatus(new SecretStatusDto(
                SecretCatalog.AccServiceApiKey,
                result.StatusLevel,
                result.Detail,
                result.Summary));

            SummaryMessage = result.Summary + (result.IsNetworkTest ? " (network test)" : " (local validation)");
            await RefreshAccControlPlaneAsync().ConfigureAwait(true);
            MessageBox.Show(
                result.Summary + Environment.NewLine + (result.Detail ?? string.Empty),
                "AccService Test",
                MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
            googleRow.JsonFileLabel = Path.GetFileName(dialog.FileName);
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
            await RefreshAccControlPlaneAsync().ConfigureAwait(true);

            var sb = new StringBuilder();
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

    private async Task RefreshAccControlPlaneAsync()
    {
        var mode = _accServiceModeProvider.Mode;
        var baseUrl = _accServiceModeProvider.BaseUrl;

        AccServiceModeSummary = mode switch
        {
            AccServiceMode.Remote when !string.IsNullOrWhiteSpace(baseUrl)
                => $"מצב ACC: שירות מרכזי ({baseUrl})",
            _ => "מצב ACC: מקומי (AccService:BaseUrl לא מוגדר)",
        };

        var keyInfo = _accServiceKeyDiagnostics.Describe();
        AccServiceKeySummary = keyInfo.HasApiKey
            ? $"מפתח ACC: קיים ב-Vault, אורך {keyInfo.KeyLength}, hash {keyInfo.KeyHashPrefix}"
            : "מפתח ACC: לא הוגדר ב-Vault.";

        if (mode != AccServiceMode.Remote || string.IsNullOrWhiteSpace(baseUrl))
        {
            AccServiceHealthSummary = "בריאות שירות ACC: לא רלוונטי במצב מקומי.";
            AccServiceDiagnosticsSummary = "אבחון ACC: מצב מקומי, ללא קריאת /v1/acc/diag.";
            return;
        }

        var health = await _accServiceHealthProbe.CheckAsync().ConfigureAwait(true);
        AccServiceHealthSummary = health.State switch
        {
            AccServiceHealthState.Online => $"בריאות שירות ACC: זמין ({health.Endpoint})",
            AccServiceHealthState.NotConfigured => "בריאות שירות ACC: לא מוגדר.",
            _ => $"בריאות שירות ACC: לא זמין ({health.Detail ?? "ללא פירוט"})",
        };

        var diagnostics = await _accServiceDiagnosticsProbe.ProbeAsync().ConfigureAwait(true);
        if (!diagnostics.Reachable)
        {
            AccServiceDiagnosticsSummary =
                $"אבחון ACC: לא זמין. Autodesk={diagnostics.AutodeskDetail ?? "ללא פירוט"}; DB={diagnostics.DbDetail ?? "ללא פירוט"}";
            return;
        }

        var keySource = string.IsNullOrWhiteSpace(diagnostics.KeySource) ? "unknown" : diagnostics.KeySource;
        var windowsUser = string.IsNullOrWhiteSpace(diagnostics.WindowsUser) ? "unknown" : diagnostics.WindowsUser;
        var keyHash = string.IsNullOrWhiteSpace(diagnostics.KeyHashPrefix) ? "(none)" : diagnostics.KeyHashPrefix;
        AccServiceDiagnosticsSummary =
            $"אבחון ACC: user={windowsUser}; keySource={keySource}; keyHash={keyHash}; Autodesk={(diagnostics.AutodeskOk ? "ok" : "fail")}; DB={(diagnostics.DbOk ? "ok" : "fail")}";
    }
}
