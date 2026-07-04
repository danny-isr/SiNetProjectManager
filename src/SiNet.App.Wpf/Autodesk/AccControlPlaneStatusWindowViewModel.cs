using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.App.Wpf.Autodesk;

public sealed class AccControlPlaneStatusWindowViewModel : ObservableObject
{
    private readonly AccControlPlaneStatusPresenter _presenter;
    private readonly IAccProjectCatalogService _accProjectCatalogService;
    private readonly IAccInboxBootstrapService _accInboxBootstrapService;
    private readonly IAccLookupSeedService _accLookupSeedService;

    private string? _hintText;
    private string _modeSummary = "טוען...";
    private string _keySummary = "טוען...";
    private string _projectsSummary = "טוען...";
    private string _healthSummary = "טוען...";
    private string _diagnosticsSummary = "טוען...";
    private string _inboxBootstrapSummary = "טרם בוצע ensure עבור ACC Inbox.";
    private string _summaryMessage = string.Empty;
    private bool _isBusy;

    public AccControlPlaneStatusWindowViewModel(
        AccControlPlaneStatusPresenter presenter,
        IAccProjectCatalogService accProjectCatalogService,
        IAccDocumentService accDocumentService,
        IAccFolderBrowserService accFolderBrowserService,
        IAccProjectTreeSearchService accProjectTreeSearchService,
        IAccLiveProjectDiscoveryService accLiveProjectDiscoveryService,
        IAccInboxBootstrapService accInboxBootstrapService,
        IAccLookupSeedService accLookupSeedService,
        IAccResolvedDocsUrlLauncher resolvedDocsUrlLauncher,
        IClipboardTextWriter clipboardTextWriter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _accProjectCatalogService = accProjectCatalogService ?? throw new ArgumentNullException(nameof(accProjectCatalogService));
        _accInboxBootstrapService = accInboxBootstrapService ?? throw new ArgumentNullException(nameof(accInboxBootstrapService));
        _accLookupSeedService = accLookupSeedService ?? throw new ArgumentNullException(nameof(accLookupSeedService));

        Browser = new AccReadOnlyDocumentBrowserViewModel(
            accDocumentService,
            accFolderBrowserService,
            accProjectTreeSearchService,
            accLiveProjectDiscoveryService,
            resolvedDocsUrlLauncher,
            clipboardTextWriter,
            isHostBusy: () => IsBusy,
            summaryMessageSink: message => SummaryMessage = message);
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && !Browser.IsBusy);
        LoadLookupSeedCommand = new AsyncRelayCommand(LoadLookupSeedAsync, () => !IsBusy && !Browser.IsBusy);
        EnsureInboxBootstrapCommand = new AsyncRelayCommand(EnsureInboxBootstrapAsync, () => !IsBusy && !Browser.IsBusy);
        Browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccReadOnlyDocumentBrowserViewModel.IsBusy))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                LoadLookupSeedCommand.RaiseCanExecuteChanged();
                EnsureInboxBootstrapCommand.RaiseCanExecuteChanged();
            }
        };
    }

    public AccReadOnlyDocumentBrowserViewModel Browser { get; }

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

    public string SummaryMessage
    {
        get => _summaryMessage;
        private set => SetField(ref _summaryMessage, value);
    }

    public string InboxBootstrapSummary
    {
        get => _inboxBootstrapSummary;
        private set => SetField(ref _inboxBootstrapSummary, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                LoadLookupSeedCommand.RaiseCanExecuteChanged();
                EnsureInboxBootstrapCommand.RaiseCanExecuteChanged();
                Browser.NotifyHostStateChanged();
            }
        }
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand LoadLookupSeedCommand { get; }

    public AsyncRelayCommand EnsureInboxBootstrapCommand { get; }

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
            await LoadBrowserProjectsAsync(presentation.KnownProjectIds).ConfigureAwait(true);
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

    public async Task LoadLookupSeedAsync()
    {
        IsBusy = true;
        try
        {
            var seeds = await _accLookupSeedService.GetRecentSeedsAsync().ConfigureAwait(true);
            var seed = seeds.FirstOrDefault();
            if (seed is null)
            {
                SummaryMessage = "לא נמצאה דוגמת lookup מתאימה ב-DB.";
                return;
            }

            Browser.ApplyLookupSeed(
                seed,
                $"נטענה דוגמה מה-DB: projectId={seed.ProjectId}; folderId={seed.FolderId}; fileName={seed.FileName}; source={seed.SourceLabel}");
            SummaryMessage = $"נטענה דוגמת lookup מה-DB ({seeds.Count} מועמדים זמינים).";
        }
        catch (Exception ex)
        {
            SummaryMessage = $"שגיאה בטעינת דוגמת lookup מה-DB: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task EnsureInboxBootstrapAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _accInboxBootstrapService.EnsureAsync().ConfigureAwait(true);
            Browser.LookupProjectId = result.AccProjectId;
            Browser.LookupFolderId = result.AccInboxFolderId;
            Browser.LookupFileName = string.Empty;
            InboxBootstrapSummary =
                $"בוצע ensure בהצלחה: hubId={result.HubId}; projectId={result.AccProjectId}; rootFolderId={result.AccRootFolderId}; inboxFolderId={result.AccInboxFolderId}";
            SummaryMessage = "בוצע Ensure ACC Inbox. שדות projectId ו-folderId עודכנו לפי התוצאה.";
        }
        catch (Exception ex)
        {
            InboxBootstrapSummary = $"שגיאה ב-Ensure ACC Inbox: {ex.Message}";
            SummaryMessage = InboxBootstrapSummary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadBrowserProjectsAsync(IReadOnlyList<string> fallbackProjectIds)
    {
        try
        {
            var projects = await _accProjectCatalogService.GetProjectsAsync().ConfigureAwait(true);
            if (projects.Count > 0)
            {
                Browser.LoadKnownProjects(projects);
                return;
            }
        }
        catch
        {
            // Fall back to plain ID list so the status window remains usable even if the richer catalog fails.
        }

        Browser.LoadKnownProjectIds(fallbackProjectIds);
    }
}
