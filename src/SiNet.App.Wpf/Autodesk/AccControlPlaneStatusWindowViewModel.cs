using System.Collections.ObjectModel;
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
    private readonly IAccInboxReconciliationService? _accInboxReconciliationService;

    private string? _hintText;
    private string _modeSummary = "טוען...";
    private string _keySummary = "טוען...";
    private string _projectsSummary = "טוען...";
    private string _healthSummary = "טוען...";
    private string _diagnosticsSummary = "טוען...";
    private string _inboxBootstrapSummary = "טרם בוצע ensure עבור ACC Inbox.";
    private string _reconcileMessageIdText = string.Empty;
    private string _reconcileMessageUniqueIdText = string.Empty;
    private string _reconciliationSummary = "טרם בוצעה בדיקת reconciliation מול ACC Inbox.";
    private string _summaryMessage = string.Empty;
    private string? _lastReconciliationProjectId;
    private bool _isBusy;
    private AccInboxReconciliationRowViewModel? _selectedReconciliationItem;

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
        IClipboardTextWriter clipboardTextWriter,
        IAccInboxReconciliationService? accInboxReconciliationService = null)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _accProjectCatalogService = accProjectCatalogService ?? throw new ArgumentNullException(nameof(accProjectCatalogService));
        _accInboxBootstrapService = accInboxBootstrapService ?? throw new ArgumentNullException(nameof(accInboxBootstrapService));
        _accLookupSeedService = accLookupSeedService ?? throw new ArgumentNullException(nameof(accLookupSeedService));
        _accInboxReconciliationService = accInboxReconciliationService;

        Browser = new AccReadOnlyDocumentBrowserViewModel(
            accDocumentService,
            accFolderBrowserService,
            accProjectTreeSearchService,
            accLiveProjectDiscoveryService,
            resolvedDocsUrlLauncher,
            clipboardTextWriter,
            isHostBusy: () => IsBusy,
            summaryMessageSink: message => SummaryMessage = message);
        ReconciliationItems = [];
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && !Browser.IsBusy);
        LoadLookupSeedCommand = new AsyncRelayCommand(LoadLookupSeedAsync, () => !IsBusy && !Browser.IsBusy);
        EnsureInboxBootstrapCommand = new AsyncRelayCommand(EnsureInboxBootstrapAsync, () => !IsBusy && !Browser.IsBusy);
        ReconcileInboxMessageCommand = new AsyncRelayCommand(ReconcileInboxMessageAsync, CanReconcileInboxMessage);
        UseSelectedReconciliationItemCommand = new RelayCommand(_ => UseSelectedReconciliationItem(), _ => CanUseSelectedReconciliationItem());
        Browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccReadOnlyDocumentBrowserViewModel.IsBusy))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                LoadLookupSeedCommand.RaiseCanExecuteChanged();
                EnsureInboxBootstrapCommand.RaiseCanExecuteChanged();
                ReconcileInboxMessageCommand.RaiseCanExecuteChanged();
            }
        };
    }

    public AccReadOnlyDocumentBrowserViewModel Browser { get; }

    public ObservableCollection<AccInboxReconciliationRowViewModel> ReconciliationItems { get; }

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

    public string ReconcileMessageIdText
    {
        get => _reconcileMessageIdText;
        set
        {
            if (SetField(ref _reconcileMessageIdText, value))
            {
                ReconcileInboxMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ReconcileMessageUniqueIdText
    {
        get => _reconcileMessageUniqueIdText;
        set
        {
            if (SetField(ref _reconcileMessageUniqueIdText, value))
            {
                ReconcileInboxMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ReconciliationSummary
    {
        get => _reconciliationSummary;
        private set => SetField(ref _reconciliationSummary, value);
    }

    public AccInboxReconciliationRowViewModel? SelectedReconciliationItem
    {
        get => _selectedReconciliationItem;
        set
        {
            if (SetField(ref _selectedReconciliationItem, value))
            {
                UseSelectedReconciliationItemCommand.RaiseCanExecuteChanged();
            }
        }
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
                ReconcileInboxMessageCommand.RaiseCanExecuteChanged();
                UseSelectedReconciliationItemCommand.RaiseCanExecuteChanged();
                Browser.NotifyHostStateChanged();
            }
        }
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand LoadLookupSeedCommand { get; }

    public AsyncRelayCommand EnsureInboxBootstrapCommand { get; }

    public AsyncRelayCommand ReconcileInboxMessageCommand { get; }

    public RelayCommand UseSelectedReconciliationItemCommand { get; }

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

    public async Task ReconcileInboxMessageAsync()
    {
        if (_accInboxReconciliationService is null)
        {
            ReconciliationSummary = "שירות ACC reconciliation אינו זמין בהוסט הנוכחי.";
            SummaryMessage = ReconciliationSummary;
            return;
        }

        IsBusy = true;
        try
        {
            AccInboxReconciliationResult? result;
            var messageIdText = ReconcileMessageIdText.Trim();
            var messageUniqueId = ReconcileMessageUniqueIdText.Trim();

            if (int.TryParse(messageIdText, out var messageId) && messageId > 0)
            {
                result = await _accInboxReconciliationService
                    .ReconcileByMessageIdAsync(messageId)
                    .ConfigureAwait(true);
            }
            else if (!string.IsNullOrWhiteSpace(messageUniqueId))
            {
                result = await _accInboxReconciliationService
                    .ReconcileByMessageUniqueIdAsync(messageUniqueId)
                    .ConfigureAwait(true);
            }
            else
            {
                ReconciliationSummary = "יש להזין EmailInboxMessage.Id או MessageUniqueId.";
                SummaryMessage = ReconciliationSummary;
                return;
            }

            ReconciliationItems.Clear();
            _lastReconciliationProjectId = result?.InboxAccProjectId;

            if (result is null)
            {
                ReconciliationSummary = "לא נמצאה הודעת Inbox תואמת לבדיקת reconciliation.";
                SummaryMessage = ReconciliationSummary;
                return;
            }

            foreach (var item in result.Attachments
                         .OrderBy(static item => item.AttachmentIndex)
                         .ThenBy(static item => item.FileName, StringComparer.OrdinalIgnoreCase))
            {
                ReconciliationItems.Add(new AccInboxReconciliationRowViewModel(
                    item.InboxAttachmentId,
                    item.AttachmentIndex,
                    item.FileName,
                    item.StatusText,
                    item.Status,
                    item.ExistsInAcc,
                    item.AccItemId,
                    item.OpenAccProjectId,
                    item.OpenAccFolderId,
                    item.OpenAccItemId,
                    item.MetadataReadFailed));
            }

            if (string.IsNullOrWhiteSpace(Browser.LookupProjectId) && !string.IsNullOrWhiteSpace(result.InboxAccProjectId))
            {
                Browser.LookupProjectId = result.InboxAccProjectId;
            }

            var existing = result.Attachments.Count(static item => item.ExistsInAcc);
            var missing = result.Attachments.Count(static item =>
                item.Status is AccInboxAttachmentPresenceStatus.MissingInAcc or AccInboxAttachmentPresenceStatus.UnknownAccInboxFile);
            ReconciliationSummary =
                $"בוצע reconciliation: messageId={result.EmailMessageId}; attachments={result.Attachments.Count}; exists={existing}; missing-or-unknown={missing}.";
            SummaryMessage = "ACC reconciliation הושלם. ניתן לבחור שורה ולהשליך אותה ל-lookup/browse.";
        }
        catch (Exception ex)
        {
            ReconciliationSummary = $"שגיאה ב-ACC reconciliation: {ex.Message}";
            SummaryMessage = ReconciliationSummary;
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

    private bool CanReconcileInboxMessage() =>
        !IsBusy
        && !Browser.IsBusy
        && _accInboxReconciliationService is not null
        && (!string.IsNullOrWhiteSpace(ReconcileMessageIdText) || !string.IsNullOrWhiteSpace(ReconcileMessageUniqueIdText));

    private bool CanUseSelectedReconciliationItem() =>
        !IsBusy
        && !Browser.IsBusy
        && SelectedReconciliationItem is not null
        && (!string.IsNullOrWhiteSpace(SelectedReconciliationItem.OpenAccProjectId) || !string.IsNullOrWhiteSpace(_lastReconciliationProjectId));

    private void UseSelectedReconciliationItem()
    {
        var item = SelectedReconciliationItem;
        if (item is null)
        {
            return;
        }

        Browser.LookupProjectId = item.OpenAccProjectId ?? _lastReconciliationProjectId ?? Browser.LookupProjectId;
        Browser.LookupFolderId = item.OpenAccFolderId ?? string.Empty;
        Browser.LookupFileName = item.FileName;
        SummaryMessage =
            $"שורת reconciliation נטענה ל-lookup: projectId={Browser.LookupProjectId}; folderId={Browser.LookupFolderId}; fileName={Browser.LookupFileName}.";
    }
}
