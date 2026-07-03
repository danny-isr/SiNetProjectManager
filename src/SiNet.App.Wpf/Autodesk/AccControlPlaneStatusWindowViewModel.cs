using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.App.Wpf.Autodesk;

public sealed class AccControlPlaneStatusWindowViewModel : ObservableObject
{
    private readonly AccControlPlaneStatusPresenter _presenter;
    private readonly IAccDocumentService _accDocumentService;
    private string? _hintText;
    private string _modeSummary = "טוען...";
    private string _keySummary = "טוען...";
    private string _projectsSummary = "טוען...";
    private string _healthSummary = "טוען...";
    private string _diagnosticsSummary = "טוען...";
    private string _lookupProjectId = string.Empty;
    private string _lookupFolderId = string.Empty;
    private string _lookupFileName = string.Empty;
    private string _lookupResultSummary = "טרם בוצע חיפוש פריט ACC.";
    private string _summaryMessage = string.Empty;
    private bool _isBusy;

    public AccControlPlaneStatusWindowViewModel(
        AccControlPlaneStatusPresenter presenter,
        IAccDocumentService accDocumentService)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _accDocumentService = accDocumentService ?? throw new ArgumentNullException(nameof(accDocumentService));
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        ResolveDocumentCommand = new AsyncRelayCommand(ResolveDocumentAsync, CanResolveDocument);
    }

    public string? HintText
    {
        get => _hintText;
        private set => SetField(ref _hintText, value);
    }

    public string ModeSummary
    {
        get => _modeSummary;
        private set => SetField(ref _modeSummary, value);
    }

    public string KeySummary
    {
        get => _keySummary;
        private set => SetField(ref _keySummary, value);
    }

    public string ProjectsSummary
    {
        get => _projectsSummary;
        private set => SetField(ref _projectsSummary, value);
    }

    public string HealthSummary
    {
        get => _healthSummary;
        private set => SetField(ref _healthSummary, value);
    }

    public string DiagnosticsSummary
    {
        get => _diagnosticsSummary;
        private set => SetField(ref _diagnosticsSummary, value);
    }

    public string LookupProjectId
    {
        get => _lookupProjectId;
        set
        {
            if (SetField(ref _lookupProjectId, value))
            {
                ResolveDocumentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LookupFolderId
    {
        get => _lookupFolderId;
        set
        {
            if (SetField(ref _lookupFolderId, value))
            {
                ResolveDocumentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LookupFileName
    {
        get => _lookupFileName;
        set
        {
            if (SetField(ref _lookupFileName, value))
            {
                ResolveDocumentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LookupResultSummary
    {
        get => _lookupResultSummary;
        private set => SetField(ref _lookupResultSummary, value);
    }

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
                RefreshCommand.RaiseCanExecuteChanged();
                ResolveDocumentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ResolveDocumentCommand { get; }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var presentation = await _presenter
                .BuildAsync(AccControlPlaneStatusPresentationKind.StatusWindow)
                .ConfigureAwait(true);

            HintText = presentation.Hint;
            ModeSummary = presentation.ModeSummary;
            KeySummary = presentation.KeySummary;
            ProjectsSummary = presentation.ProjectsSummary;
            HealthSummary = presentation.HealthSummary;
            DiagnosticsSummary = presentation.DiagnosticsSummary;
            SummaryMessage = "סטטוס ACC נטען.";
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

    private bool CanResolveDocument() =>
        !string.IsNullOrWhiteSpace(LookupProjectId)
        && !string.IsNullOrWhiteSpace(LookupFolderId)
        && !string.IsNullOrWhiteSpace(LookupFileName);

    public async Task ResolveDocumentAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _accDocumentService
                .FindItemAsync(
                    LookupProjectId.Trim(),
                    LookupFolderId.Trim(),
                    LookupFileName.Trim())
                .ConfigureAwait(true);

            if (result is null)
            {
                LookupResultSummary = "פריט ACC לא נמצא עבור projectId + folderId + fileName שסופקו.";
            }
            else
            {
                var versionText = string.IsNullOrWhiteSpace(result.VersionId) ? "(none)" : result.VersionId;
                var viewerText = string.IsNullOrWhiteSpace(result.ViewerUrl) ? "(none)" : result.ViewerUrl;
                LookupResultSummary =
                    $"נמצא פריט ACC: projectId={result.ProjectId}; itemId={result.ItemId}; versionId={versionText}; viewerUrl={viewerText}";
            }

            SummaryMessage = "בדיקת lookup של פריט ACC הושלמה.";
        }
        catch (Exception ex)
        {
            LookupResultSummary = $"שגיאה ב-lookup של פריט ACC: {ex.Message}";
            SummaryMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
